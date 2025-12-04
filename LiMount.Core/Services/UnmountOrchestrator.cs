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
        ArgumentNullException.ThrowIfNull(scriptExecutor);
        _scriptExecutor = scriptExecutor;
        _historyService = historyService;
    }

    /// <summary>
    /// Coordinates unmapping an optional drive letter and unmounting the specified disk from WSL, reporting progress as it proceeds.
    /// </summary>
    /// <param name="diskIndex">Index of the disk to unmount from WSL.</param>
    /// <param name="driveLetter">Optional drive letter to unmap before unmounting; pass null to skip unmapping.</param>
    /// <param name="progress">Optional progress reporter that receives status messages.</param>
    /// <returns>An UnmountAndUnmapResult indicating success (including disk index and optional drive letter) or failure (including disk index, an error message, and the failed operation identifier). The returned drive letter (if any) is only populated when the unmapping operation succeeds. If unmapping fails but unmounting succeeds, the result will have Success = false with FailedStep = "unmap" to indicate the workflow did not complete successfully.</returns>
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
        bool unmappingFailed = false;
        string? unmappingError = null;
        
        if (driveLetter.HasValue)
        {
            progress?.Report($"Unmapping drive letter {driveLetter}:...");

            var unmappingResult = await _scriptExecutor.ExecuteUnmappingScriptAsync(driveLetter.Value);

            if (!unmappingResult.Success)
            {
                progress?.Report($"Drive unmapping failed: {unmappingResult.ErrorMessage}");
                unmappingFailed = true;
                unmappingError = unmappingResult.ErrorMessage;
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

            // Log failure to history (best-effort)
            await LogToHistoryAsync(failureResult);

            return failureResult;
        }

        progress?.Report("Disk unmounted successfully from WSL");

        // Create result - handle partial failure case
        UnmountAndUnmapResult result;
        if (unmappingFailed)
        {
            // Partial failure: unmount succeeded but unmap failed
            // Treat as overall failure since the workflow didn't complete fully
            result = UnmountAndUnmapResult.CreateFailure(
                diskIndex,
                unmappingError ?? "Drive unmapping failed",
                "unmap");
        }
        else
        {
            // Complete success
            result = UnmountAndUnmapResult.CreateSuccess(diskIndex,
                string.IsNullOrEmpty(unmappedDriveLetter) ? null : unmappedDriveLetter[0]);
        }

        // Log to history (best-effort)
        await LogToHistoryAsync(result);

        return result;
    }

    /// <summary>
    /// Logs the operation result to history service in a best-effort manner.
    /// Exceptions from the history service are caught and logged but do not fail the operation.
    /// </summary>
    /// <param name="result">The result to log to history.</param>
    private async Task LogToHistoryAsync(UnmountAndUnmapResult result)
    {
        if (_historyService == null) return;

        try
        {
            var historyEntry = MountHistoryEntry.FromUnmountResult(result);
            await _historyService.AddEntryAsync(historyEntry);
        }
        catch (Exception ex)
        {
            // History logging is best-effort - don't fail the operation
            // In a real implementation, you'd use ILogger here
            System.Diagnostics.Debug.WriteLine($"Failed to log to history: {ex.Message}");
        }
    }
}