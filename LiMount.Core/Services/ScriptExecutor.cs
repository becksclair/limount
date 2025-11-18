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

    public ScriptExecutor(string? scriptsPath = null)
    {
        _scriptsPath = scriptsPath ?? FindScriptsPath();
    }

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

            // Temp output file not found - treat as error regardless of exit code
            return $"STATUS=ERROR\nErrorMessage=Elevated script produced no output. Expected temp file: {tempOutputFile}, Process exit code: {process.ExitCode}";
        }
        catch (Exception ex)
        {
            return $"STATUS=ERROR\nErrorMessage={ex.Message}";
        }
    }

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
