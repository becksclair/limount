using System.Runtime.Versioning;
using LiMount.Core.Interfaces;
using LiMount.Core.Models;

namespace LiMount.Core.Services;

/// <summary>
/// Orchestrates the complete unmount workflow: drive letter unmapping + WSL unmounting.
/// </summary>
[SupportedOSPlatform("windows")]
public class UnmountOrchestrator : IUnmountOrchestrator
{
    private readonly IScriptExecutor _scriptExecutor;

    public UnmountOrchestrator(IScriptExecutor scriptExecutor)
    {
        _scriptExecutor = scriptExecutor ?? throw new ArgumentNullException(nameof(scriptExecutor));
    }

    public async Task<UnmountAndUnmapResult> UnmountAndUnmapAsync(
        int diskIndex,
        char? driveLetter = null,
        IProgress<string>? progress = null)
    {
        progress?.Report($"Starting unmount operation for disk {diskIndex}...");

        // Step 1: Unmap drive letter (if provided)
        if (driveLetter.HasValue)
        {
            progress?.Report($"Unmapping drive letter {driveLetter}:...");

            var unmappingResult = await _scriptExecutor.ExecuteUnmappingScriptAsync(driveLetter.Value);

            if (!unmappingResult.Success)
            {
                progress?.Report($"Drive unmapping failed: {unmappingResult.ErrorMessage}");
                // Continue anyway - we still want to try unmounting from WSL
            }
            else
            {
                progress?.Report($"Drive letter {driveLetter}: unmapped successfully");
            }
        }

        // Step 2: Unmount from WSL
        progress?.Report("Unmounting disk from WSL...");

        var unmountResult = await _scriptExecutor.ExecuteUnmountScriptAsync(diskIndex);

        if (!unmountResult.Success)
        {
            progress?.Report($"Unmount failed: {unmountResult.ErrorMessage}");
            return UnmountAndUnmapResult.CreateFailure(
                diskIndex,
                unmountResult.ErrorMessage ?? "Unknown error during unmount",
                "unmount");
        }

        progress?.Report("Disk unmounted successfully from WSL");

        // Return success
        return UnmountAndUnmapResult.CreateSuccess(diskIndex, driveLetter);
    }
}
