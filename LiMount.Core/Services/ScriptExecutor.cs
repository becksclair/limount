using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using LiMount.Core.Interfaces;
using LiMount.Core.Models;

namespace LiMount.Core.Services;

/// <summary>
/// Executes PowerShell scripts for mounting/unmounting operations.
/// Uses ProcessStartInfo with elevation where needed.
/// </summary>
[SupportedOSPlatform("windows")]
public class ScriptExecutor : IScriptExecutor
{
    /// <summary>
    /// Maximum time to wait for elevated script output temp file to appear.
    /// </summary>
    private const int TempFilePollingTimeoutSeconds = 5;

    /// <summary>
    /// Interval between checks when polling for temp file.
    /// </summary>
    private const int TempFilePollingIntervalMs = 100;

    private readonly string _scriptsPath;
    private readonly ILogger<ScriptExecutor>? _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="ScriptExecutor"/> and sets the scripts directory path.
    /// </summary>
    /// <param name="scriptsPath">Optional explicit path to the directory containing PowerShell scripts; if null, the implementation locates a suitable scripts directory automatically.</param>
    /// <param name="logger">Optional logger for diagnostic information.</param>
    public ScriptExecutor(string? scriptsPath = null, ILogger<ScriptExecutor>? logger = null)
    {
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
    /// <returns>A MountResult describing the outcome; `Success` is `true` when the disk was mounted, `false` otherwise. `ErrorMessage` contains details on failure.</returns>
    public async Task<MountResult> ExecuteMountScriptAsync(
        int diskIndex,
        int partition,
        string fsType,
        string? distroName = null)
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
        var arguments = $"-ExecutionPolicy Bypass -File \"{scriptPath}\" " +
                       $"-DiskIndex {diskIndex} -Partition {partition} -FsType {trimmedFsType}";

        if (!string.IsNullOrEmpty(distroName))
        {
            arguments += $" -DistroName \"{distroName}\"";
        }

        var output = await ExecuteElevatedScriptAsync("powershell.exe", arguments, diskIndex, partition);
        var parsedValues = KeyValueOutputParser.Parse(output);
        return MountResult.FromDictionary(parsedValues);
    }

    /// <summary>
    /// Maps a WSL network share to the specified drive letter by running the Map-WSLShareToDrive.ps1 PowerShell script.
    /// </summary>
    /// <param name="driveLetter">The drive letter to assign (for example, 'Z').</param>
    /// <param name="targetUNC">The UNC path of the WSL share to map (for example, \\wsl$\distro\share).</param>
    /// <returns>A MappingResult containing whether the mapping succeeded, any error message, and the script's raw output and error.</returns>
    public async Task<MappingResult> ExecuteMappingScriptAsync(char driveLetter, string targetUNC)
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

        var (output, error) = await ExecuteNonElevatedScriptAsync("powershell.exe", arguments);
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
    /// <returns>An <see cref="UnmountResult"/> containing whether the operation succeeded and any error information.</returns>
    public async Task<UnmountResult> ExecuteUnmountScriptAsync(int diskIndex)
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

        var arguments = $"-ExecutionPolicy Bypass -File \"{scriptPath}\" -DiskIndex {diskIndex}";

        var output = await ExecuteElevatedScriptAsync("powershell.exe", arguments, diskIndex, 0, isUnmountOperation: true);
        var parsedValues = KeyValueOutputParser.Parse(output);
        return UnmountResult.FromDictionary(parsedValues);
    }

    /// <summary>
    /// Invokes the Unmap-DriveLetter PowerShell script to unmap the specified drive letter and returns the operation result.
    /// </summary>
    /// <returns>
    /// An <see cref="UnmappingResult"/> representing the outcome; when the script is missing or the script output indicates failure, `Success` will be `false` and `ErrorMessage` will contain details.
    /// </returns>
    public async Task<UnmappingResult> ExecuteUnmappingScriptAsync(char driveLetter)
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

        var (output, error) = await ExecuteNonElevatedScriptAsync("powershell.exe", arguments);
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
    /// <param name="diskIndex">Disk index used to construct the temporary output filename.</param>
    /// <param name="partition">Partition number used to construct the temporary output filename.</param>
    /// <param name="isUnmountOperation">Whether this is an unmount operation (uses different filename pattern).</param>
    /// <returns>The contents of the temporary output file when successful; otherwise a string beginning with "STATUS=ERROR" and an error message.</returns>
    private async Task<string> ExecuteElevatedScriptAsync(
        string fileName,
        string arguments,
        int diskIndex,
        int partition,
        bool isUnmountOperation = false)
    {
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

            await process.WaitForExitAsync();

            // Read output from temp file (script writes there for elevated scenarios)
            var tempOutputFile = isUnmountOperation
                ? Path.Combine(Path.GetTempPath(), $"limount_unmount_{diskIndex}.txt")
                : Path.Combine(Path.GetTempPath(), $"limount_mount_{diskIndex}_{partition}.txt");

            // Wait for file to be written
            var timeout = TimeSpan.FromSeconds(TempFilePollingTimeoutSeconds);
            var pollingInterval = TimeSpan.FromMilliseconds(TempFilePollingIntervalMs);
            var totalWaitTime = TimeSpan.Zero;

            while (totalWaitTime < timeout)
            {
                if (File.Exists(tempOutputFile))
                    break;

                await Task.Delay(pollingInterval);
                totalWaitTime += pollingInterval;
            }

            if (File.Exists(tempOutputFile))
            {
                var output = await File.ReadAllTextAsync(tempOutputFile);
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

            // Temp output file not found - treat as error regardless of exit code
            return $"STATUS=ERROR\nErrorMessage=Elevated script produced no output. Expected temp file: {tempOutputFile}, Process exit code: {process.ExitCode}";
        }
        catch (Exception ex)
        {
            return $"STATUS=ERROR\nErrorMessage={ex.Message}";
        }
    }

    /// <summary>
    /// Runs a process (typically PowerShell) without elevation and captures its standard output and standard error.
    /// </summary>
    /// <param name="fileName">The executable to run (for example, "powershell.exe").</param>
    /// <param name="arguments">The complete argument string to pass to the process.</param>
    /// <returns>
    /// A tuple where `output` is the process's standard output and `error` is the process's standard error.
    /// If the process fails to start or an exception occurs, `output` contains an error status message and `error` is an empty string.
    /// </returns>
    private async Task<(string output, string error)> ExecuteNonElevatedScriptAsync(string fileName, string arguments)
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

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            var exitTask = process.WaitForExitAsync();

            await Task.WhenAll(outputTask, errorTask, exitTask);

            var output = await outputTask;
            var error = await errorTask;

            return (output, error);
        }
        catch (Exception ex)
        {
            return ($"STATUS=ERROR\nErrorMessage={ex.Message}", string.Empty);
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
}