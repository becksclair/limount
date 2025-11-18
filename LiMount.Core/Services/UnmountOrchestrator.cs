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

    /// <summary>
    /// Initializes a new instance of UnmountOrchestrator with the provided script executor.
    /// </summary>
    /// <param name="scriptExecutor">The executor used to run unmapping and unmounting scripts; must not be null.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="scriptExecutor"/> is null.</exception>
    public UnmountOrchestrator(IScriptExecutor scriptExecutor)
    {
        _scriptExecutor = scriptExecutor ?? throw new ArgumentNullException(nameof(scriptExecutor));
    }

    /// <summary>
    /// Coordinates unmapping an optional drive letter and unmounting the specified disk from WSL, reporting progress as it proceeds.
    /// </summary>
    /// <param name="diskIndex">Index of the disk to unmount from WSL.</param>
    /// <param name="driveLetter">Optional drive letter to unmap before unmounting; pass null to skip unmapping.</param>
    /// <param name="progress">Optional progress reporter that receives status messages.</param>
    /// <returns>An UnmountAndUnmapResult indicating success (including disk index and optional drive letter) or failure (including disk index, an error message, and the failed operation identifier).</returns>
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