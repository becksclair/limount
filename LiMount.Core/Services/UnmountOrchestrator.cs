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
    private readonly IMountHistoryService? _historyService;

    /// <summary>
    /// Initializes a new instance of UnmountOrchestrator with the provided script executor.
    /// </summary>
    /// <param name="scriptExecutor">The executor used to run unmapping and unmounting scripts; must not be null.</param>
    /// <param name="historyService">Optional history service for tracking unmount operations.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="scriptExecutor"/> is null.</exception>
    public UnmountOrchestrator(IScriptExecutor scriptExecutor, IMountHistoryService? historyService = null)
    {
        _scriptExecutor = scriptExecutor ?? throw new ArgumentNullException(nameof(scriptExecutor));
        _historyService = historyService;
    }

    /// <summary>
    /// Coordinates unmapping an optional drive letter and unmounting the specified disk from WSL, reporting progress as it proceeds.
    /// </summary>
    /// <param name="diskIndex">Index of the disk to unmount from WSL.</param>
    /// <param name="driveLetter">Optional drive letter to unmap before unmounting; pass null to skip unmapping.</param>
    /// <param name="progress">Optional progress reporter that receives status messages.</param>
    /// <returns>An UnmountAndUnmapResult indicating success (including disk index and optional drive letter) or failure (including disk index, an error message, and the failed operation identifier). The returned drive letter (if any) is the same one passed as input and may be present even if the unmapping operation failed. Callers should inspect the operation result/failure details to determine whether unmapping succeeded rather than relying on the presence of a drive letter.</returns>
    public async Task<UnmountAndUnmapResult> UnmountAndUnmapAsync(
        int diskIndex,
        char? driveLetter = null,
        IProgress<string>? progress = null)
    {
        // Validate parameters
        if (diskIndex < 0)
        {
            return UnmountAndUnmapResult.CreateFailure(diskIndex, "Disk index must be non-negative", "validation");
        }

        if (driveLetter.HasValue && !char.IsLetter(driveLetter.Value))
        {
            return UnmountAndUnmapResult.CreateFailure(diskIndex, "Drive letter must be a valid letter (A-Z)", "validation");
        }

        progress?.Report($"Starting unmount operation for disk {diskIndex}...");

        // Step 1: Unmap drive letter (if provided)
        string? unmappedDriveLetter = null;
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
                unmappedDriveLetter = driveLetter.Value.ToString();
            }
        }

        // Step 2: Unmount from WSL
        progress?.Report("Unmounting disk from WSL...");

        var unmountResult = await _scriptExecutor.ExecuteUnmountScriptAsync(diskIndex);

        if (!unmountResult.Success)
        {
            progress?.Report($"Unmount failed: {unmountResult.ErrorMessage}");
            var failureResult = UnmountAndUnmapResult.CreateFailure(
                diskIndex,
                unmountResult.ErrorMessage ?? "Unknown error during unmount",
                "unmount");

            // Log failure to history
            if (_historyService != null)
            {
                var historyEntry = MountHistoryEntry.FromUnmountResult(failureResult);
                await _historyService.AddEntryAsync(historyEntry);
            }

            return failureResult;
        }

        progress?.Report("Disk unmounted successfully from WSL");

        // Create success result
        var result = UnmountAndUnmapResult.CreateSuccess(diskIndex,
            string.IsNullOrEmpty(unmappedDriveLetter) ? null : unmappedDriveLetter[0]);

        // Log to history
        if (_historyService != null)
        {
            var historyEntry = MountHistoryEntry.FromUnmountResult(result);
            await _historyService.AddEntryAsync(historyEntry);
        }

        return result;
    }
}