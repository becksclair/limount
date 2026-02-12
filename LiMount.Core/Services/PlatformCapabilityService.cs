using System.Diagnostics;
using System.Management;
using System.Runtime.Versioning;
using LiMount.Core.Interfaces;
using LiMount.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace LiMount.Core.Services;

/// <summary>
/// Generic command execution result used by platform capability probes.
/// </summary>
public readonly record struct PlatformCommandResult(int ExitCode, string StdOut, string StdErr);

/// <summary>
/// Detects WSL and virtualization capabilities for setup UX.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class PlatformCapabilityService : IPlatformCapabilityService
{
    private const int CommandTimeoutMs = 8000;
    private readonly ILogger<PlatformCapabilityService>? _logger;
    private readonly Func<string, string, System.Text.Encoding?, CancellationToken, Task<PlatformCommandResult>> _commandRunner;
    private readonly Func<string> _windowsEditionProvider;
    private readonly Func<long> _memoryProvider;
    private readonly Func<string, bool> _fileExists;
    private readonly Func<RegistryHive, RegistryView, string, string, string?> _registryValueProvider;
    private readonly Func<string, bool> _serviceExists;
    private readonly Func<bool> _hypervisorPresentProvider;

    /// <summary>
    /// Initializes a new instance of <see cref="PlatformCapabilityService"/>.
    /// </summary>
    public PlatformCapabilityService(ILogger<PlatformCapabilityService>? logger = null)
        : this(logger, null, null, null, null, null)
    {
    }

    internal PlatformCapabilityService(
        ILogger<PlatformCapabilityService>? logger,
        Func<string, string, System.Text.Encoding?, CancellationToken, Task<PlatformCommandResult>>? commandRunner,
        Func<string>? windowsEditionProvider,
        Func<long>? memoryProvider,
        Func<string, bool>? fileExists,
        Func<RegistryHive, RegistryView, string, string, string?>? registryValueProvider,
        Func<string, bool>? serviceExists = null,
        Func<bool>? hypervisorPresentProvider = null)
    {
        _logger = logger;
        _commandRunner = commandRunner ?? RunProcessAsync;
        _windowsEditionProvider = windowsEditionProvider ?? GetWindowsEdition;
        _memoryProvider = memoryProvider ?? GetHostMemoryBytes;
        _fileExists = fileExists ?? File.Exists;
        _registryValueProvider = registryValueProvider ?? ReadRegistryValue;
        _serviceExists = serviceExists ?? ServiceExists;
        _hypervisorPresentProvider = hypervisorPresentProvider ?? IsHypervisorPresent;
    }

    public async Task<PlatformCapabilities> DetectAsync(CancellationToken cancellationToken = default)
    {
        var capabilities = new PlatformCapabilities
        {
            WindowsEdition = _windowsEditionProvider(),
            HostCpuCores = Math.Max(1, Environment.ProcessorCount),
            HostMemoryBytes = _memoryProvider()
        };

        capabilities.HyperVSupported = IsHyperVEditionSupported(capabilities.WindowsEdition);
        capabilities.HyperVEnabled = await DetectHyperVFeatureEnabledAsync(cancellationToken);
        capabilities.HyperVCmdletsAvailable = await DetectHyperVCmdletsAsync(cancellationToken);

        capabilities.WslInstalled = await DetectWslInstalledAsync(cancellationToken);
        capabilities.WslMountSupported = capabilities.WslInstalled && await DetectWslMountSupportAsync(cancellationToken);
        capabilities.DefaultDistroPresent = capabilities.WslInstalled && await DetectWslDistroPresenceAsync(cancellationToken);

        capabilities.VmwareInstalled = await DetectVmwareInstalledAsync(cancellationToken);
        capabilities.VirtualBoxInstalled = await DetectVirtualBoxInstalledAsync(cancellationToken);

        capabilities.HyperVUnavailableReason = BuildHyperVUnavailableReason(capabilities);
        capabilities.VmwareUnavailableReason = capabilities.VmwareInstalled
            ? null
            : "vmrun.exe was not found in PATH, registry install locations, or Program Files.";
        capabilities.VirtualBoxUnavailableReason = capabilities.VirtualBoxInstalled
            ? null
            : "VBoxManage.exe was not found in PATH, registry install locations, or Program Files.";
        capabilities.WslUnavailableReason = BuildWslUnavailableReason(capabilities);

        return capabilities;
    }

    internal static bool IsHyperVEditionSupported(string edition)
    {
        if (string.IsNullOrWhiteSpace(edition))
        {
            return false;
        }

        var normalized = edition.Trim().ToLowerInvariant();
        if (normalized.Contains("home", StringComparison.Ordinal))
        {
            return false;
        }

        return normalized.Contains("pro", StringComparison.Ordinal) ||
               normalized.Contains("enterprise", StringComparison.Ordinal) ||
               normalized.Contains("education", StringComparison.Ordinal) ||
               normalized.Contains("server", StringComparison.Ordinal) ||
               normalized.Contains("professional", StringComparison.Ordinal) ||
               normalized.Contains("workstation", StringComparison.Ordinal);
    }

    internal static string? BuildHyperVUnavailableReason(PlatformCapabilities capabilities)
    {
        if (!capabilities.HyperVSupported)
        {
            return "Hyper-V is not supported on this Windows edition.";
        }

        if (!capabilities.HyperVEnabled)
        {
            return "Hyper-V feature is currently disabled.";
        }

        if (!capabilities.HyperVCmdletsAvailable)
        {
            return "Hyper-V management tooling is unavailable.";
        }

        return null;
    }

    internal static string? BuildWslUnavailableReason(PlatformCapabilities capabilities)
    {
        if (!capabilities.WslInstalled)
        {
            return "WSL is not installed.";
        }

        if (!capabilities.WslMountSupported)
        {
            return "wsl --mount support was not detected.";
        }

        if (!capabilities.DefaultDistroPresent)
        {
            return "No WSL distributions were detected.";
        }

        return null;
    }

    private static string GetWindowsEdition()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            var value = key?.GetValue("EditionID")?.ToString();
            return string.IsNullOrWhiteSpace(value) ? "Unknown" : value;
        }
        catch
        {
            return "Unknown";
        }
    }

    private static long GetHostMemoryBytes()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem");
            foreach (var item in searcher.Get().Cast<ManagementObject>())
            {
                var value = item["TotalPhysicalMemory"]?.ToString();
                if (long.TryParse(value, out var bytes) && bytes > 0)
                {
                    return bytes;
                }
            }
        }
        catch
        {
            // Fall through to runtime-based estimate.
        }

        var runtimeValue = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        return runtimeValue > 0 ? runtimeValue : 0;
    }

    private async Task<bool> CommandExistsAsync(string commandName, CancellationToken cancellationToken)
    {
        var result = await _commandRunner(
            "where.exe",
            commandName,
            null,
            cancellationToken);

        return result.ExitCode == 0 && !string.IsNullOrWhiteSpace(result.StdOut);
    }

    private async Task<bool> DetectVmwareInstalledAsync(CancellationToken cancellationToken)
    {
        if (await CommandExistsAsync("vmrun.exe", cancellationToken))
        {
            return true;
        }

        if (AnyCandidateExists(GetVmwareProgramFilesCandidates()))
        {
            return true;
        }

        var installPath = GetFirstRegistryValue(
            valueName: "InstallPath",
            @"SOFTWARE\VMware, Inc.\VMware Workstation",
            @"SOFTWARE\WOW6432Node\VMware, Inc.\VMware Workstation");

        if (TryBuildExecutablePathFromInstallPath(installPath, "vmrun.exe", out var vmrunPath) && _fileExists(vmrunPath))
        {
            return true;
        }

        return false;
    }

    private async Task<bool> DetectVirtualBoxInstalledAsync(CancellationToken cancellationToken)
    {
        if (await CommandExistsAsync("VBoxManage.exe", cancellationToken))
        {
            return true;
        }

        if (AnyCandidateExists(GetVirtualBoxProgramFilesCandidates()))
        {
            return true;
        }

        var installDir = GetFirstRegistryValue(
            valueName: "InstallDir",
            @"SOFTWARE\Oracle\VirtualBox",
            @"SOFTWARE\WOW6432Node\Oracle\VirtualBox")
            ?? GetFirstRegistryValue(
                valueName: "InstallPath",
                @"SOFTWARE\Oracle\VirtualBox",
                @"SOFTWARE\WOW6432Node\Oracle\VirtualBox");

        if (TryBuildExecutablePathFromInstallPath(installDir, "VBoxManage.exe", out var vboxManagePath) && _fileExists(vboxManagePath))
        {
            return true;
        }

        return false;
    }

    private IEnumerable<string> GetVmwareProgramFilesCandidates()
    {
        foreach (var basePath in EnumerateProgramFilesRoots())
        {
            yield return Path.Combine(basePath, "VMware", "VMware Workstation", "vmrun.exe");
            yield return Path.Combine(basePath, "VMware", "VMware Player", "vmrun.exe");
        }
    }

    private IEnumerable<string> GetVirtualBoxProgramFilesCandidates()
    {
        foreach (var basePath in EnumerateProgramFilesRoots())
        {
            yield return Path.Combine(basePath, "Oracle", "VirtualBox", "VBoxManage.exe");
        }
    }

    private IEnumerable<string> EnumerateProgramFilesRoots()
    {
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
        };

        return roots.Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private bool AnyCandidateExists(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            if (_fileExists(path))
            {
                return true;
            }
        }

        return false;
    }

    private string? GetFirstRegistryValue(string valueName, params string[] subKeyPaths)
    {
        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        {
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                foreach (var subKeyPath in subKeyPaths)
                {
                    var value = _registryValueProvider(hive, view, subKeyPath, valueName);
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        return value;
                    }
                }
            }
        }

        return null;
    }

    private static bool TryBuildExecutablePathFromInstallPath(string? installPath, string executableName, out string executablePath)
    {
        executablePath = string.Empty;
        if (string.IsNullOrWhiteSpace(installPath))
        {
            return false;
        }

        var expanded = Environment.ExpandEnvironmentVariables(installPath).Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(expanded))
        {
            return false;
        }

        if (expanded.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            executablePath = expanded;
            return true;
        }

        executablePath = Path.Combine(expanded, executableName);
        return true;
    }

    private static string? ReadRegistryValue(RegistryHive hive, RegistryView view, string subKeyPath, string valueName)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var subKey = baseKey.OpenSubKey(subKeyPath);
            return subKey?.GetValue(valueName)?.ToString();
        }
        catch
        {
            return null;
        }
    }

    private async Task<bool> DetectHyperVFeatureEnabledAsync(CancellationToken cancellationToken)
    {
        var result = await _commandRunner(
            "dism.exe",
            "/Online /Get-FeatureInfo /FeatureName:Microsoft-Hyper-V-All",
            null,
            cancellationToken);

        if (result.ExitCode == 0)
        {
            var parsedState = ParseDismHyperVFeatureEnabled(result.StdOut);
            if (parsedState.HasValue)
            {
                return parsedState.Value;
            }
        }

        // Some environments block DISM probing in user context. Fall back to non-admin runtime checks.
        return DetectHyperVEnabledWithoutElevation();
    }

    private Task<bool> DetectHyperVCmdletsAsync(CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        return Task.FromResult(DetectHyperVManagementToolsPresent());
    }

    private async Task<bool> DetectWslInstalledAsync(CancellationToken cancellationToken)
    {
        var result = await _commandRunner("wsl.exe", "--status", null, cancellationToken);
        return result.ExitCode == 0;
    }

    private async Task<bool> DetectWslMountSupportAsync(CancellationToken cancellationToken)
    {
        var helpResult = await _commandRunner(
            "wsl.exe",
            "--help",
            System.Text.Encoding.Unicode,
            cancellationToken);

        if (OutputContainsMountFlag(helpResult))
        {
            return true;
        }

        // Some WSL builds emit help/usage details on stderr for subcommand help.
        var mountHelpResult = await _commandRunner(
            "wsl.exe",
            "--mount --help",
            System.Text.Encoding.Unicode,
            cancellationToken);

        if (OutputContainsMountFlag(mountHelpResult))
        {
            return true;
        }

        // Last-resort probe: if the parser recognizes the mount verb, usage text usually includes "--mount".
        var mountProbeResult = await _commandRunner(
            "wsl.exe",
            "--mount",
            System.Text.Encoding.Unicode,
            cancellationToken);

        return OutputContainsMountFlag(mountProbeResult);
    }

    private async Task<bool> DetectWslDistroPresenceAsync(CancellationToken cancellationToken)
    {
        var result = await _commandRunner(
            "wsl.exe",
            "--list --quiet",
            System.Text.Encoding.Unicode,
            cancellationToken);

        if (result.ExitCode != 0)
        {
            return false;
        }

        var lines = result.StdOut
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return lines.Length > 0;
    }

    private static bool? ParseDismHyperVFeatureEnabled(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return null;
        }

        if (output.Contains("State : Enabled", StringComparison.OrdinalIgnoreCase) ||
            output.Contains("State : Enable Pending", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (output.Contains("State : Disabled", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return null;
    }

    private bool DetectHyperVEnabledWithoutElevation()
    {
        var hypervisorPresent = _hypervisorPresentProvider();
        var vmmsServicePresent = _serviceExists("vmms");
        var vmComputeServicePresent = _serviceExists("vmcompute");

        // Treat Hyper-V as enabled when runtime hypervisor is active or core services are installed.
        return hypervisorPresent || vmmsServicePresent || vmComputeServicePresent;
    }

    private bool DetectHyperVManagementToolsPresent()
    {
        if (_serviceExists("vmms") || _serviceExists("vmcompute"))
        {
            return true;
        }

        foreach (var modulePath in GetHyperVModuleCandidates())
        {
            if (_fileExists(modulePath))
            {
                return true;
            }
        }

        return false;
    }

    private IEnumerable<string> GetHyperVModuleCandidates()
    {
        var windowsPath = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var programFilesPath = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

        if (!string.IsNullOrWhiteSpace(windowsPath))
        {
            yield return Path.Combine(windowsPath, "System32", "WindowsPowerShell", "v1.0", "Modules", "Hyper-V", "Hyper-V.psd1");
            yield return Path.Combine(windowsPath, "SysNative", "WindowsPowerShell", "v1.0", "Modules", "Hyper-V", "Hyper-V.psd1");
        }

        if (!string.IsNullOrWhiteSpace(programFilesPath))
        {
            yield return Path.Combine(programFilesPath, "WindowsPowerShell", "Modules", "Hyper-V", "Hyper-V.psd1");
        }
    }

    private static bool ServiceExists(string serviceName)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                $"SELECT Name FROM Win32_Service WHERE Name = '{serviceName.Replace("'", "''")}'");
            using var services = searcher.Get();
            return services.Count > 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsHypervisorPresent()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT HypervisorPresent FROM Win32_ComputerSystem");
            foreach (var item in searcher.Get().Cast<ManagementObject>())
            {
                if (item["HypervisorPresent"] is bool isPresent)
                {
                    return isPresent;
                }
            }
        }
        catch
        {
            // ignore and return false
        }

        return false;
    }

    private async Task<PlatformCommandResult> RunProcessAsync(
        string fileName,
        string arguments,
        System.Text.Encoding? standardOutputEncoding,
        CancellationToken cancellationToken)
    {
        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            if (standardOutputEncoding != null)
            {
                process.StartInfo.StandardOutputEncoding = standardOutputEncoding;
                process.StartInfo.StandardErrorEncoding = standardOutputEncoding;
            }

            process.Start();

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(CommandTimeoutMs);

            var stdoutTask = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
            var stderrTask = process.StandardError.ReadToEndAsync(timeoutCts.Token);
            await process.WaitForExitAsync(timeoutCts.Token);

            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            return new PlatformCommandResult(process.ExitCode, stdout, stderr);
        }
        catch (OperationCanceledException)
        {
            return new PlatformCommandResult(-1, string.Empty, "Command timed out.");
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Capability detection command failed: {FileName} {Arguments}", fileName, arguments);
            return new PlatformCommandResult(-1, string.Empty, ex.Message);
        }
    }

    private static bool OutputContainsMountFlag(PlatformCommandResult result)
    {
        var combinedOutput = NormalizeCommandOutput(
            string.Concat(result.StdOut ?? string.Empty, "\n", result.StdErr ?? string.Empty));
        return combinedOutput.Contains("--mount", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeCommandOutput(string output)
    {
        if (string.IsNullOrEmpty(output))
        {
            return string.Empty;
        }

        // WSL CLI output can be UTF-16 decoded via UTF-8 paths, leaving interleaved NUL chars.
        return output.Replace("\0", string.Empty);
    }
}
