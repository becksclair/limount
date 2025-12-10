using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using LiMount.Core.Interfaces;
using LiMount.Core.Models;
using LiMount.Core.Configuration;

namespace LiMount.Core.Services;

/// <summary>
/// Executes PowerShell scripts for mounting/unmounting operations.
/// Uses ProcessStartInfo with elevation where needed.
/// </summary>
/// <remarks>
/// Implements focused interfaces for single responsibility:
/// <see cref="IMountScriptService"/> for mount/unmount operations,
/// <see cref="IDriveMappingService"/> for drive letter mapping,
/// <see cref="IFilesystemDetectionService"/> for filesystem detection.
/// Also implements deprecated <see cref="IScriptExecutor"/> for backward compatibility.
/// </remarks>
[SupportedOSPlatform("windows")]
#pragma warning disable CS0618 // IScriptExecutor is obsolete - intentionally implementing for backward compatibility
public class ScriptExecutor : IMountScriptService, IDriveMappingService, IFilesystemDetectionService, IScriptExecutor
#pragma warning restore CS0618
{
    // Compiled regex patterns for efficient parsing of lsblk output
    private static readonly Regex NameRegex = new(@"NAME=""([^""]+)""", RegexOptions.Compiled);
    private static readonly Regex FsTypeRegex = new(@"FSTYPE=""([^""]*)""", RegexOptions.Compiled);

    private readonly string _scriptsPath;
    private readonly ILogger<ScriptExecutor>? _logger;
    private readonly ScriptExecutionConfig _config;

    /// <summary>
    /// Initializes a new instance of <see cref="ScriptExecutor"/> and sets the scripts directory path.
    /// </summary>
    /// <param name="config">Configuration for script execution.</param>
    /// <param name="scriptsPath">Optional explicit path to the directory containing PowerShell scripts; if null, the implementation locates a suitable scripts directory automatically.</param>
    /// <param name="logger">Optional logger for diagnostic information.</param>
    public ScriptExecutor(
        IOptions<LiMountConfiguration> config,
        string? scriptsPath = null,
        ILogger<ScriptExecutor>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        _config = config.Value.ScriptExecution;
        _scriptsPath = scriptsPath ?? FindScriptsPath();
        _logger = logger;
    }

    /// <summary>
    /// Mounts a Linux disk partition and returns the operation result.
    /// </summary>
    /// <param name="diskIndex">Index of the physical disk to mount.</param>
    /// <param name="partition">Partition number on the disk to mount.</param>
    /// <param name="fsType">Filesystem type to mount (for example, "ext4").</param>
    /// <param name="distroName">Optional WSL distribution name to associate with the mount.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A MountResult describing the outcome; `Success` is `true` when the disk was mounted, `false` otherwise. `ErrorMessage` contains details on failure.</returns>
    public async Task<MountResult> ExecuteMountScriptAsync(
        int diskIndex,
        int partition,
        string fsType,
        string? distroName = null,
        CancellationToken cancellationToken = default)
    {
        if (diskIndex < 0)
        {
            return new MountResult
            {
                Success = false,
                ErrorMessage = "Disk index must be non-negative"
            };
        }

        if (partition < 1)
        {
            return new MountResult
            {
                Success = false,
                ErrorMessage = "Partition number must be greater than 0"
            };
        }

        if (string.IsNullOrWhiteSpace(fsType))
        {
            return new MountResult
            {
                Success = false,
                ErrorMessage = "Invalid filesystem type"
            };
        }

        var scriptPath = Path.Combine(_scriptsPath, "Mount-LinuxDiskCore.ps1");
        if (!File.Exists(scriptPath))
        {
            return new MountResult
            {
                Success = false,
                ErrorMessage = $"Mount script not found at: {scriptPath}"
            };
        }

        var trimmedFsType = fsType.Trim();

        // Generate GUID-based temp file path BEFORE script execution to prevent
        // predictable temp file attacks (CWE-377: Insecure Temporary File)
        var tempFileId = Guid.NewGuid().ToString("N");
        var tempOutputFile = Path.Combine(Path.GetTempPath(), $"limount_mount_{tempFileId}.txt");

        var arguments = $"-ExecutionPolicy Bypass -File \"{scriptPath}\" " +
                       $"-DiskIndex {diskIndex} -Partition {partition} -FsType {trimmedFsType} " +
                       $"-OutputFile \"{tempOutputFile}\"";

        if (!string.IsNullOrEmpty(distroName))
        {
            arguments += $" -DistroName \"{distroName}\"";
        }

        var output = await ExecuteElevatedScriptAsync("powershell.exe", arguments, tempOutputFile, cancellationToken);
        var parsedValues = KeyValueOutputParser.Parse(output);
        return MountResult.FromDictionary(parsedValues);
    }

    /// <summary>
    /// Maps a WSL network share to the specified drive letter by running the Map-WSLShareToDrive.ps1 PowerShell script.
    /// </summary>
    /// <param name="driveLetter">The drive letter to assign (for example, 'Z').</param>
    /// <param name="targetUNC">The UNC path of the WSL share to map (for example, \\wsl$\distro\share).</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A MappingResult containing whether the mapping succeeded, any error message, and the script's raw output and error.</returns>
    public async Task<MappingResult> ExecuteMappingScriptAsync(char driveLetter, string targetUNC, CancellationToken cancellationToken = default)
    {
        if (!char.IsLetter(driveLetter))
        {
            return new MappingResult
            {
                Success = false,
                ErrorMessage = "Drive letter must be a valid letter (A-Z)"
            };
        }

        if (string.IsNullOrWhiteSpace(targetUNC))
        {
            return new MappingResult
            {
                Success = false,
                ErrorMessage = "Target UNC path cannot be empty"
            };
        }

        var scriptPath = Path.Combine(_scriptsPath, "Map-WSLShareToDrive.ps1");
        if (!File.Exists(scriptPath))
        {
            return new MappingResult
            {
                Success = false,
                ErrorMessage = $"Mapping script not found at: {scriptPath}"
            };
        }

        var arguments = $"-ExecutionPolicy Bypass -File \"{scriptPath}\" " +
                       $"-DriveLetter {driveLetter} -TargetUNC \"{targetUNC}\"";

        var (output, error) = await ExecuteNonElevatedScriptAsync("powershell.exe", arguments, cancellationToken);
        var parsedValues = KeyValueOutputParser.Parse(output);
        var result = MappingResult.FromDictionary(parsedValues);

        // Include raw output for debugging and error handling
        result.RawOutput = output;
        result.RawError = error;

        // If parsing failed but there's stderr output, include it in error message
        if (!result.Success && !string.IsNullOrEmpty(error))
        {
            result.ErrorMessage = string.IsNullOrEmpty(result.ErrorMessage)
                ? error
                : $"{result.ErrorMessage}\n{error}";
        }

        return result;
    }

    /// <summary>
    /// Unmounts the Linux disk identified by the given disk index by invoking the Unmount-LinuxDisk.ps1 script.
    /// </summary>
    /// <param name="diskIndex">The disk index to unmount.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>An <see cref="UnmountResult"/> containing whether the operation succeeded and any error information.</returns>
    public async Task<UnmountResult> ExecuteUnmountScriptAsync(int diskIndex, CancellationToken cancellationToken = default)
    {
        if (diskIndex < 0)
        {
            return new UnmountResult
            {
                Success = false,
                ErrorMessage = "Disk index must be non-negative"
            };
        }

        var scriptPath = Path.Combine(_scriptsPath, "Unmount-LinuxDisk.ps1");
        if (!File.Exists(scriptPath))
        {
            return new UnmountResult
            {
                Success = false,
                ErrorMessage = $"Unmount script not found at: {scriptPath}"
            };
        }

        // Generate GUID-based temp file path BEFORE script execution to prevent
        // predictable temp file attacks (CWE-377: Insecure Temporary File)
        var tempFileId = Guid.NewGuid().ToString("N");
        var tempOutputFile = Path.Combine(Path.GetTempPath(), $"limount_unmount_{tempFileId}.txt");

        var arguments = $"-ExecutionPolicy Bypass -File \"{scriptPath}\" -DiskIndex {diskIndex} " +
                       $"-OutputFile \"{tempOutputFile}\"";

        var output = await ExecuteElevatedScriptAsync("powershell.exe", arguments, tempOutputFile, cancellationToken);
        var parsedValues = KeyValueOutputParser.Parse(output);
        return UnmountResult.FromDictionary(parsedValues);
    }

    /// <summary>
    /// Invokes the Unmap-DriveLetter PowerShell script to unmap the specified drive letter and returns the operation result.
    /// </summary>
    /// <param name="driveLetter">Drive letter to unmap (e.g., 'Z').</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>
    /// An <see cref="UnmappingResult"/> representing the outcome; when the script is missing or the script output indicates failure, `Success` will be `false` and `ErrorMessage` will contain details.
    /// </returns>
    public async Task<UnmappingResult> ExecuteUnmappingScriptAsync(char driveLetter, CancellationToken cancellationToken = default)
    {
        if (!char.IsLetter(driveLetter))
        {
            return new UnmappingResult
            {
                Success = false,
                ErrorMessage = "Drive letter must be a valid letter (A-Z)"
            };
        }

        var scriptPath = Path.Combine(_scriptsPath, "Unmap-DriveLetter.ps1");
        if (!File.Exists(scriptPath))
        {
            return new UnmappingResult
            {
                Success = false,
                ErrorMessage = $"Unmapping script not found at: {scriptPath}"
            };
        }

        var arguments = $"-ExecutionPolicy Bypass -File \"{scriptPath}\" -DriveLetter {driveLetter}";

        var (output, error) = await ExecuteNonElevatedScriptAsync("powershell.exe", arguments, cancellationToken);
        var parsedValues = KeyValueOutputParser.Parse(output);
        var result = UnmappingResult.FromDictionary(parsedValues);

        // Include raw output for debugging and error handling
        result.RawOutput = output;
        result.RawError = error;

        // If parsing failed but there's stderr output, include it in error message
        if (!result.Success && !string.IsNullOrEmpty(error))
        {
            result.ErrorMessage = string.IsNullOrEmpty(result.ErrorMessage)
                ? error
                : $"{result.ErrorMessage}\n{error}";
        }

        return result;
    }

    /// <summary>
    /// Runs the specified executable with elevation and returns the script output written to a temporary file.
    /// </summary>
    /// <param name="fileName">The executable to run (e.g., "powershell.exe").</param>
    /// <param name="arguments">The arguments to pass to the executable.</param>
    /// <param name="tempOutputFile">The GUID-based temp file path where the script will write output.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The contents of the temporary output file when successful; otherwise a string beginning with "STATUS=ERROR" and an error message.</returns>
    private async Task<string> ExecuteElevatedScriptAsync(
        string fileName,
        string arguments,
        string tempOutputFile,
        CancellationToken cancellationToken = default)
    {
        // SECURITY AUDIT: Log all elevated operation requests for audit trail
        var correlationId = Path.GetFileNameWithoutExtension(tempOutputFile);
        _logger?.LogInformation(
            "SECURITY AUDIT: Elevated operation requested. CorrelationId={CorrelationId}, Executable={FileName}, TempFile={TempFile}, Timestamp={Timestamp:O}",
            correlationId, fileName, tempOutputFile, DateTime.UtcNow);

        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            Verb = "runas", // Request elevation
            UseShellExecute = true, // Required for elevation
            CreateNoWindow = false,
            WindowStyle = ProcessWindowStyle.Hidden
        };

        try
        {
            using var process = Process.Start(startInfo);
            if (process == null)
            {
                return "STATUS=ERROR\nErrorMessage=Failed to start PowerShell process";
            }

            await process.WaitForExitAsync(cancellationToken);

            // Wait for the GUID-based temp file to be written by the script
            var normalizedTimeoutSeconds = Math.Max(0, Math.Min(_config.TempFilePollingTimeoutSeconds, 300));
            var normalizedPollingIntervalMs = Math.Max(1, Math.Min(_config.PollingIntervalMs, 10000));

            var timeout = TimeSpan.FromSeconds(normalizedTimeoutSeconds);
            var pollingInterval = TimeSpan.FromMilliseconds(normalizedPollingIntervalMs);
            var totalWaitTime = TimeSpan.Zero;

            while (totalWaitTime < timeout)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (File.Exists(tempOutputFile))
                {
                    break;
                }

                await Task.Delay(pollingInterval, cancellationToken);
                totalWaitTime += pollingInterval;
            }

            if (File.Exists(tempOutputFile))
            {
                try
                {
                    var output = await File.ReadAllTextAsync(tempOutputFile, cancellationToken);
                    try
                    {
                        File.Delete(tempOutputFile);
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogWarning(ex, "Failed to delete temp file {TempFile}", tempOutputFile);
                    }
                    return output;
                }
                catch (IOException ex)
                {
                    _logger?.LogWarning(ex, "Failed to read temp file {TempFile}, may be locked by another process", tempOutputFile);
                    return $"STATUS=ERROR\nErrorMessage=Failed to read output file: {ex.Message}";
                }
            }

            // Temp output file not found - treat as error regardless of exit code
            return $"STATUS=ERROR\nErrorMessage=Elevated script produced no output. Expected temp file: {tempOutputFile}, Process exit code: {process.ExitCode}";
        }
        catch (OperationCanceledException)
        {
            throw; // Let cancellation propagate
        }
        catch (OutOfMemoryException)
        {
            throw; // Critical: OOM should not be swallowed
        }
        catch (Exception ex)
        {
            var errorDetails = ex.InnerException != null
                ? $"{ex.Message} (Inner: {ex.InnerException.Message})"
                : ex.Message;
            return $"STATUS=ERROR\nErrorMessage={errorDetails}";
        }
    }

    /// <summary>
    /// Runs a process (typically PowerShell) without elevation and captures its standard output and standard error.
    /// </summary>
    /// <param name="fileName">The executable to run (for example, "powershell.exe").</param>
    /// <param name="arguments">The complete argument string to pass to the process.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>
    /// A tuple where `output` is the process's standard output and `error` is the process's standard error.
    /// If the process fails to start or an exception occurs, `output` contains an error status message and `error` is an empty string.
    /// </returns>
    private async Task<(string output, string error)> ExecuteNonElevatedScriptAsync(string fileName, string arguments, CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        try
        {
            using var process = Process.Start(startInfo);
            if (process == null)
            {
                return ("STATUS=ERROR\nErrorMessage=Failed to start PowerShell process", string.Empty);
            }

            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            var exitTask = process.WaitForExitAsync(cancellationToken);

            await Task.WhenAll(outputTask, errorTask, exitTask);

            var output = await outputTask;
            var error = await errorTask;

            return (output, error);
        }
        catch (OperationCanceledException)
        {
            throw; // Let cancellation propagate
        }
        catch (OutOfMemoryException)
        {
            throw; // Critical: OOM should not be swallowed
        }
        catch (Exception ex)
        {
            var errorDetails = ex.InnerException != null
                ? $"{ex.Message} (Inner: {ex.InnerException.Message})"
                : ex.Message;
            return ($"STATUS=ERROR\nErrorMessage={errorDetails}", string.Empty);
        }
    }

    /// <summary>
    /// Locate the directory that contains the PowerShell scripts used by the executor.
    /// </summary>
    /// <returns>The full path to the first existing candidate scripts directory. If none of the checked locations exist, returns the "scripts" subdirectory under the application's base directory.</returns>
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

        // Fallback
        return Path.Combine(appDirectory, "scripts");
    }

    /// <inheritdoc/>
    public async Task<string?> DetectFilesystemTypeAsync(int diskIndex, int partitionNumber, CancellationToken cancellationToken = default)
    {
        var diskPath = $@"\\.\PHYSICALDRIVE{diskIndex}";
        _logger?.LogInformation("Detecting filesystem type for disk {DiskIndex} partition {Partition}", diskIndex, partitionNumber);

        try
        {
            // First, check if disk is already mounted by looking for its mount path
            var alreadyMounted = await IsDiskAlreadyMountedAsync(diskIndex, partitionNumber, cancellationToken);

            if (alreadyMounted)
            {
                // Disk is already mounted - just run lsblk directly
                _logger?.LogInformation("Disk {DiskIndex} is already mounted, reading filesystem type directly", diskIndex);
                var (success, output, _) = await RunWslCommandAsync("lsblk -f -o NAME,FSTYPE -P", cancellationToken);
                if (success && !string.IsNullOrEmpty(output))
                {
                    var fsType = ParseLsblkOutput(output, partitionNumber);
                    _logger?.LogInformation("Detected filesystem type: {FsType}", fsType ?? "unknown");
                    return fsType;
                }
                return null;
            }

            // Step 1: Attach disk with --bare (requires elevation)
            var (attachSuccess, attachOutput, attachError) = await RunElevatedWslCommandAsync($"--mount {diskPath} --bare", cancellationToken);
            if (!attachSuccess)
            {
                // Check if already mounted error
                if (attachError?.Contains("already mounted", StringComparison.OrdinalIgnoreCase) == true)
                {
                    _logger?.LogInformation("Disk already mounted, trying lsblk directly");
                    var (success, output, error) = await RunWslCommandAsync("lsblk -f -o NAME,FSTYPE -P", cancellationToken);
                    if (success && !string.IsNullOrEmpty(output))
                    {
                        return ParseLsblkOutput(output, partitionNumber);
                    }
                }
                _logger?.LogWarning("Failed to attach disk for filesystem detection: {Error}", attachError);
                return null;
            }

            try
            {
                // Step 2: Run lsblk to detect filesystem (no elevation needed)
                var (success, output, error) = await RunWslCommandAsync("lsblk -f -o NAME,FSTYPE -P", cancellationToken);
                if (!success || string.IsNullOrEmpty(output))
                {
                    _logger?.LogWarning("Failed to run lsblk: {Error}", error);
                    return null;
                }

                // Step 3: Parse output to find filesystem type
                // Looking for the partition that corresponds to the disk we attached
                // Format: NAME="sde1" FSTYPE="xfs"
                var fsType = ParseLsblkOutput(output, partitionNumber);
                _logger?.LogInformation("Detected filesystem type: {FsType}", fsType ?? "unknown");
                return fsType;
            }
            finally
            {
                // Step 4: Always detach the disk
                // Use cancellation token if not already cancelled, otherwise use None to allow cleanup to proceed
                var cleanupToken = cancellationToken.IsCancellationRequested
                    ? CancellationToken.None
                    : cancellationToken;
                try
                {
                    await RunElevatedWslCommandAsync($"--unmount {diskPath}", cleanupToken);
                }
                catch (OperationCanceledException)
                {
                    // If cleanup was cancelled, log but don't throw - cleanup should complete if possible
                    _logger?.LogWarning("Cleanup unmount operation was cancelled for disk {DiskIndex}", diskIndex);
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error detecting filesystem type for disk {DiskIndex}", diskIndex);
            return null;
        }
    }

    /// <summary>
    /// Checks if a disk is already mounted in WSL.
    /// </summary>
    private async Task<bool> IsDiskAlreadyMountedAsync(int diskIndex, int partitionNumber, CancellationToken cancellationToken = default)
    {
        try
        {
            // Check if the mount path exists via UNC
            var mountName = $"PHYSICALDRIVE{diskIndex}p{partitionNumber}";

            // Try to find a WSL distro and check if mount exists
            var (success, output, _) = await RunWslCommandAsync("-l -q", cancellationToken);
            if (!success || string.IsNullOrEmpty(output))
                return false;

            var distros = output
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(d => d.Trim().Replace("\0", ""))
                .Where(d => !string.IsNullOrWhiteSpace(d))
                .ToList();

            if (distros.Count == 0) return false;

            var distroName = distros[0];
            var uncPath = $@"\\wsl.localhost\{distroName}\mnt\wsl\{mountName}";

            return Directory.Exists(uncPath);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Runs an elevated WSL command (requires UAC).
    /// </summary>
    private async Task<(bool Success, string? Output, string? Error)> RunElevatedWslCommandAsync(string arguments, CancellationToken cancellationToken = default)
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"wsl_output_{Guid.NewGuid()}.txt");

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "wsl.exe",
                Arguments = arguments,
                UseShellExecute = true,
                Verb = "runas",
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            using var process = Process.Start(startInfo);
            if (process == null)
            {
                return (false, null, "Failed to start WSL process");
            }

            await process.WaitForExitAsync(cancellationToken);
            return (process.ExitCode == 0, null, process.ExitCode != 0 ? $"Exit code: {process.ExitCode}" : null);
        }
        catch (Exception ex)
        {
            return (false, null, ex.Message);
        }
    }

    /// <summary>
    /// Runs a non-elevated WSL command and captures output.
    /// </summary>
    /// <remarks>
    /// SECURITY WARNING: The <paramref name="linuxCommand"/> parameter is passed directly to
    /// wsl.exe without sanitization. This method MUST only be called with hard-coded,
    /// trusted command strings. NEVER pass user-controlled input to this method as it could
    /// lead to command injection attacks. All current usages pass hard-coded commands like
    /// "lsblk -f -o NAME,FSTYPE -P" or "-l -q" which are safe.
    /// </remarks>
    private async Task<(bool Success, string? Output, string? Error)> RunWslCommandAsync(string linuxCommand, CancellationToken cancellationToken = default)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "wsl.exe",
                // SECURITY: linuxCommand is only ever called with hard-coded strings.
                // Do not refactor to accept user input without proper validation.
                Arguments = $"-e {linuxCommand}",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process == null)
            {
                return (false, null, "Failed to start WSL process");
            }

            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            var error = await process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            return (process.ExitCode == 0, output, error);
        }
        catch (Exception ex)
        {
            return (false, null, ex.Message);
        }
    }

    /// <summary>
    /// Parses lsblk output to find the filesystem type for a partition.
    /// </summary>
    private static string? ParseLsblkOutput(string output, int partitionNumber)
    {
        // The mounted disk will be the last sd* device
        // We're looking for lines like: NAME="sde1" FSTYPE="xfs"
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        // Find the newest device (highest sd letter) with a partition number
        string? lastFsType = null;
        foreach (var line in lines)
        {
            // Look for partition entries (e.g., sd*1, sd*2)
            if (line.Contains("NAME=\"") && line.Contains("FSTYPE=\""))
            {
                // Extract NAME and FSTYPE using pre-compiled static regex patterns
                var nameMatch = NameRegex.Match(line);
                var fsTypeMatch = FsTypeRegex.Match(line);

                if (nameMatch.Success && fsTypeMatch.Success)
                {
                    var name = nameMatch.Groups[1].Value;
                    var fsType = fsTypeMatch.Groups[1].Value;

                    // Check if this is a partition (ends with a number matching our partition)
                    if (name.EndsWith(partitionNumber.ToString()) && !string.IsNullOrEmpty(fsType))
                    {
                        lastFsType = fsType;
                    }
                }
            }
        }

        return lastFsType;
    }
}