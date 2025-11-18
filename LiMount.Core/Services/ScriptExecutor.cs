using System.Diagnostics;
using System.Runtime.Versioning;
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
    private readonly string _scriptsPath;

    /// <summary>
    /// Initializes a new instance of <see cref="ScriptExecutor"/> using the provided scripts directory or locating it automatically if none is supplied.
    /// </summary>
    /// <param name="scriptsPath">Optional path to the scripts directory; if null, the constructor determines the scripts path via <see cref="FindScriptsPath"/>.</param>
    public ScriptExecutor(string? scriptsPath = null)
    {
        _scriptsPath = scriptsPath ?? FindScriptsPath();
    }

    /// <summary>
    /// Runs the PowerShell mount script for the specified Linux disk and partition and returns the parsed result.
    /// </summary>
    /// <param name="diskIndex">The target disk index to mount.</param>
    /// <param name="partition">The partition number on the disk to mount.</param>
    /// <param name="fsType">The filesystem type to use for mounting (e.g., "ext4").</param>
    /// <param name="distroName">Optional distribution name to pass to the mount script.</param>
    /// <returns>A <see cref="MountResult"/> representing the script's outcome and any values parsed from its output.</returns>
    public async Task<MountResult> ExecuteMountScriptAsync(
        int diskIndex,
        int partition,
        string fsType,
        string? distroName = null)
    {
        var scriptPath = Path.Combine(_scriptsPath, "Mount-LinuxDiskCore.ps1");
        if (!File.Exists(scriptPath))
        {
            return new MountResult
            {
                Success = false,
                ErrorMessage = $"Mount script not found at: {scriptPath}"
            };
        }

        var arguments = $"-ExecutionPolicy Bypass -File \"{scriptPath}\" " +
                       $"-DiskIndex {diskIndex} -Partition {partition} -FsType {fsType}";

        if (!string.IsNullOrEmpty(distroName))
        {
            arguments += $" -DistroName \"{distroName}\"";
        }

        var output = await ExecuteElevatedScriptAsync("powershell.exe", arguments, diskIndex, partition);
        var parsedValues = KeyValueOutputParser.Parse(output);
        return MountResult.FromDictionary(parsedValues);
    }

    /// <summary>
    /// Maps a network share to the specified drive letter by running the Map-WSLShareToDrive PowerShell script.
    /// </summary>
    /// <param name="driveLetter">The drive letter to assign to the mapped share.</param>
    /// <param name="targetUNC">The UNC path of the network share to map (for example, \\server\share).</param>
    /// <returns>`MappingResult` indicating whether the mapping succeeded and containing any error details.</returns>
    public async Task<MappingResult> ExecuteMappingScriptAsync(char driveLetter, string targetUNC)
    {
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

        var output = await ExecuteNonElevatedScriptAsync("powershell.exe", arguments);
        var parsedValues = KeyValueOutputParser.Parse(output);
        return MappingResult.FromDictionary(parsedValues);
    }

    /// <summary>
    /// Unmounts the specified Linux disk and returns the result of the operation.
    /// </summary>
    /// <param name="diskIndex">Index of the disk to unmount.</param>
    /// <returns>An <see cref="UnmountResult"/> describing success and any error message.</returns>
    public async Task<UnmountResult> ExecuteUnmountScriptAsync(int diskIndex)
    {
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

        var output = await ExecuteElevatedScriptAsync("powershell.exe", arguments, diskIndex, 0);
        var parsedValues = KeyValueOutputParser.Parse(output);
        return UnmountResult.FromDictionary(parsedValues);
    }

    /// <summary>
    /// Unmaps a drive letter by executing the Unmap-DriveLetter PowerShell script located in the configured scripts directory.
    /// </summary>
    /// <param name="driveLetter">The drive letter to unmap (e.g., 'Z').</param>
    /// <returns>An UnmappingResult containing the operation status and any error message.</returns>
    public async Task<UnmappingResult> ExecuteUnmappingScriptAsync(char driveLetter)
    {
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

        var output = await ExecuteNonElevatedScriptAsync("powershell.exe", arguments);
        var parsedValues = KeyValueOutputParser.Parse(output);
        return UnmappingResult.FromDictionary(parsedValues);
    }

    /// <summary>
    /// Executes a PowerShell process with elevation, waits for completion, and returns the script's textual result.
    /// </summary>
    /// <param name="fileName">The executable to run (e.g., "powershell.exe").</param>
    /// <param name="arguments">Command-line arguments passed to the executable, including script path and parameters.</param>
    /// <param name="diskIndex">Disk index used to construct the temporary output file name written by elevated scripts.</param>
    /// <param name="partition">Partition number used to construct the temporary output file name written by elevated scripts.</param>
    /// <returns>
    /// The script output as a raw string. If the elevated script writes a temp file named "limount_mount_{diskIndex}_{partition}.txt" it returns that file's contents; otherwise returns "STATUS=OK" on successful exit or "STATUS=ERROR\nErrorMessage=..." containing an error description or exit code.
    /// </returns>
    private async Task<string> ExecuteElevatedScriptAsync(
        string fileName,
        string arguments,
        int diskIndex,
        int partition)
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
            var tempOutputFile = Path.Combine(
                Path.GetTempPath(),
                $"limount_mount_{diskIndex}_{partition}.txt");

            // Wait for file to be written
            for (int i = 0; i < 10; i++)
            {
                if (File.Exists(tempOutputFile))
                    break;
                await Task.Delay(100);
            }

            if (File.Exists(tempOutputFile))
            {
                var output = await File.ReadAllTextAsync(tempOutputFile);
                try { File.Delete(tempOutputFile); } catch { /* Ignore */ }
                return output;
            }

            // Fallback based on exit code
            if (process.ExitCode == 0)
            {
                return "STATUS=OK";
            }

            return $"STATUS=ERROR\nErrorMessage=Script exited with code {process.ExitCode}";
        }
        catch (Exception ex)
        {
            return $"STATUS=ERROR\nErrorMessage={ex.Message}";
        }
    }

    /// <summary>
    /// Executes a process without elevation and captures its standard output or a standardized error string.
    /// </summary>
    /// <returns>The process's standard output. If the process fails to start or an exception occurs, a string beginning with "STATUS=ERROR" and an ErrorMessage entry describing the failure.</returns>
    private async Task<string> ExecuteNonElevatedScriptAsync(string fileName, string arguments)
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
                return "STATUS=ERROR\nErrorMessage=Failed to start PowerShell process";
            }

            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            return output;
        }
        catch (Exception ex)
        {
            return $"STATUS=ERROR\nErrorMessage={ex.Message}";
        }
    }

    /// <summary>
    /// Locates the repository's "scripts" directory by checking several common relative locations from the application's base directory.
    /// </summary>
    /// <returns>The full path to the first existing "scripts" directory found. If none of the checked locations exist, returns the path formed by appending "scripts" to the application's base directory.</returns>
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