using System.Diagnostics;
using System.Runtime.Versioning;
using LiMount.Core.Interfaces;
using LiMount.Core.Models;
using LiMount.Core.Results;
using Microsoft.Extensions.Logging;

namespace LiMount.Core.Services.Access;

/// <summary>
/// Creates/removes Windows access surfaces for mounted UNC paths.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsAccessService : IWindowsAccessService
{
    private const string PowerShellExe = "powershell.exe";

    private readonly IDriveMappingService _driveMappingService;
    private readonly ILogger<WindowsAccessService>? _logger;
    private readonly string _scriptsPath;

    public WindowsAccessService(
        IDriveMappingService driveMappingService,
        ILogger<WindowsAccessService>? logger = null,
        string? scriptsPath = null)
    {
        _driveMappingService = driveMappingService ?? throw new ArgumentNullException(nameof(driveMappingService));
        _logger = logger;
        _scriptsPath = scriptsPath ?? FindScriptsPath();
    }

    public async Task<Result<WindowsAccessInfo>> CreateAccessAsync(
        WindowsAccessRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.AccessMode == WindowsAccessMode.None)
        {
            return Result<WindowsAccessInfo>.Success(new WindowsAccessInfo
            {
                AccessMode = WindowsAccessMode.None,
                AccessPathUNC = request.TargetUNC ?? string.Empty
            });
        }

        if (string.IsNullOrWhiteSpace(request.TargetUNC))
        {
            return Result<WindowsAccessInfo>.Failure("Target UNC path cannot be empty.", "map");
        }

        if (request.AccessMode == WindowsAccessMode.DriveLetterLegacy)
        {
            if (!request.DriveLetter.HasValue || !char.IsLetter(request.DriveLetter.Value))
            {
                return Result<WindowsAccessInfo>.Failure("Drive letter is required for legacy drive-letter mode.", "validation");
            }

            var mapping = await _driveMappingService.ExecuteMappingScriptAsync(
                request.DriveLetter.Value,
                request.TargetUNC,
                cancellationToken);

            if (!mapping.Success)
            {
                return Result<WindowsAccessInfo>.Failure(
                    mapping.ErrorMessage ?? "Failed to map drive letter.",
                    "map");
            }

            return Result<WindowsAccessInfo>.Success(new WindowsAccessInfo
            {
                AccessMode = WindowsAccessMode.DriveLetterLegacy,
                AccessPathUNC = request.TargetUNC,
                DriveLetter = request.DriveLetter
            });
        }

        var networkLocationName = BuildNetworkLocationName(request);
        var createScriptResult = await ExecuteNetworkScriptAsync(
            "Create-NetworkLocation.ps1",
            $"-Name '{EscapePowerShellStringLiteral(networkLocationName)}' -TargetUNC '{EscapePowerShellStringLiteral(request.TargetUNC)}'",
            cancellationToken);

        if (createScriptResult.IsFailure)
        {
            return Result<WindowsAccessInfo>.Failure(
                createScriptResult.ErrorMessage ?? "Failed to create network location.",
                "map");
        }

        var createValues = createScriptResult.Value!;
        if (!KeyValueOutputParser.IsSuccess(createValues))
        {
            return Result<WindowsAccessInfo>.Failure(
                KeyValueOutputParser.GetErrorMessage(createValues) ?? "Failed to create network location.",
                "map");
        }

        return Result<WindowsAccessInfo>.Success(new WindowsAccessInfo
        {
            AccessMode = WindowsAccessMode.NetworkLocation,
            AccessPathUNC = request.TargetUNC,
            NetworkLocationName = createValues.TryGetValue("NetworkLocationName", out var createdName)
                ? createdName
                : networkLocationName
        });
    }

    public async Task<Result> RemoveAccessAsync(
        WindowsAccessInfo accessInfo,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(accessInfo);

        if (accessInfo.AccessMode == WindowsAccessMode.None)
        {
            return Result.Success();
        }

        if (accessInfo.AccessMode == WindowsAccessMode.DriveLetterLegacy)
        {
            if (!accessInfo.DriveLetter.HasValue || !char.IsLetter(accessInfo.DriveLetter.Value))
            {
                return Result.Failure("Drive letter is required to remove legacy drive mapping.", "unmap");
            }

            var unmapping = await _driveMappingService.ExecuteUnmappingScriptAsync(
                accessInfo.DriveLetter.Value,
                cancellationToken);

            return unmapping.Success
                ? Result.Success()
                : Result.Failure(unmapping.ErrorMessage ?? "Failed to unmap drive letter.", "unmap");
        }

        if (string.IsNullOrWhiteSpace(accessInfo.NetworkLocationName))
        {
            // Nothing to remove.
            return Result.Success();
        }

        var removeScriptResult = await ExecuteNetworkScriptAsync(
            "Remove-NetworkLocation.ps1",
            $"-Name '{EscapePowerShellStringLiteral(accessInfo.NetworkLocationName)}'",
            cancellationToken);

        if (removeScriptResult.IsFailure)
        {
            return Result.Failure(removeScriptResult.ErrorMessage ?? "Failed to remove network location.", "unmap");
        }

        var removeValues = removeScriptResult.Value!;
        if (!KeyValueOutputParser.IsSuccess(removeValues))
        {
            return Result.Failure(
                KeyValueOutputParser.GetErrorMessage(removeValues) ?? "Failed to remove network location.",
                "unmap");
        }

        return Result.Success();
    }

    private async Task<Result<Dictionary<string, string>>> ExecuteNetworkScriptAsync(
        string scriptName,
        string arguments,
        CancellationToken cancellationToken)
    {
        var scriptPath = Path.Combine(_scriptsPath, "network", scriptName);
        if (!File.Exists(scriptPath))
        {
            return Result<Dictionary<string, string>>.Failure($"Network script not found at: {scriptPath}");
        }

        var psi = new ProcessStartInfo
        {
            FileName = PowerShellExe,
            Arguments = $"-ExecutionPolicy Bypass -File \"{scriptPath}\" {arguments}",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        try
        {
            using var process = Process.Start(psi);
            if (process == null)
            {
                return Result<Dictionary<string, string>>.Failure("Failed to start PowerShell process.");
            }

            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

            await process.WaitForExitAsync(cancellationToken);

            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            var values = KeyValueOutputParser.Parse(stdout);

            if (!string.IsNullOrWhiteSpace(stderr) && values.Count == 0)
            {
                _logger?.LogWarning("Network script {ScriptName} failed with stderr: {Error}", scriptName, stderr.Trim());
                return Result<Dictionary<string, string>>.Failure(stderr.Trim());
            }

            return Result<Dictionary<string, string>>.Success(values);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed executing network script {ScriptName}", scriptName);
            return Result<Dictionary<string, string>>.Failure(ex.Message);
        }
    }

    private static string BuildNetworkLocationName(WindowsAccessRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.NetworkLocationName))
        {
            return SanitizeNetworkLocationName(request.NetworkLocationName);
        }

        return $"LiMount Disk {request.DiskIndex} Partition {request.PartitionNumber}";
    }

    private static string SanitizeNetworkLocationName(string value)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = new string(value.Trim().Select(ch => invalidChars.Contains(ch) ? '_' : ch).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "LiMount Mount" : sanitized;
    }

    private static string EscapePowerShellStringLiteral(string value)
    {
        return (value ?? string.Empty).Replace("'", "''", StringComparison.Ordinal);
    }

    private static string FindScriptsPath()
    {
        var appDirectory = AppDomain.CurrentDomain.BaseDirectory;

        var possiblePaths = new[]
        {
            Path.Combine(appDirectory, "scripts"),
            Path.Combine(appDirectory, "..", "..", "..", "scripts"),
            Path.Combine(appDirectory, "..", "..", "..", "..", "scripts"),
            Path.Combine(appDirectory, "..", "..", "..", "..", "..", "scripts")
        };

        foreach (var path in possiblePaths)
        {
            var fullPath = Path.GetFullPath(path);
            if (Directory.Exists(fullPath))
            {
                return fullPath;
            }
        }

        return Path.Combine(appDirectory, "scripts");
    }
}

