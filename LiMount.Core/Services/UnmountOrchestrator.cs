using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using LiMount.Core.Interfaces;
using LiMount.Core.Models;
using LiMount.Core.Results;

namespace LiMount.Core.Services;

/// <summary>
/// Orchestrates the complete unmount workflow: Windows access removal + WSL unmounting.
/// </summary>
[SupportedOSPlatform("windows")]
public class UnmountOrchestrator : IUnmountOrchestrator
{
    private readonly IMountScriptService _mountScriptService;
    private readonly IWindowsAccessService _windowsAccessService;
    private readonly IMountHistoryService? _historyService;
    private readonly ILogger<UnmountOrchestrator>? _logger;

    /// <summary>
    /// Initializes a new instance of UnmountOrchestrator with the provided services.
    /// </summary>
    /// <param name="mountScriptService">Service for executing unmount scripts.</param>
    /// <param name="windowsAccessService">Service for Windows access removal operations.</param>
    /// <param name="historyService">Optional history service for tracking unmount operations.</param>
    /// <param name="logger">Optional logger for diagnostic information.</param>
    /// <exception cref="ArgumentNullException">Thrown when required parameters are null.</exception>
    public UnmountOrchestrator(
        IMountScriptService mountScriptService,
        IWindowsAccessService windowsAccessService,
        IMountHistoryService? historyService = null,
        ILogger<UnmountOrchestrator>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(mountScriptService);
        ArgumentNullException.ThrowIfNull(windowsAccessService);
        _mountScriptService = mountScriptService;
        _windowsAccessService = windowsAccessService;
        _historyService = historyService;
        _logger = logger;
    }

    /// <summary>
    /// Coordinates Windows access removal and unmounting the specified disk from WSL, reporting progress as it proceeds.
    /// </summary>
    /// <param name="diskIndex">Index of the disk to unmount from WSL.</param>
    /// <param name="accessMode">Windows access mode used for the active mount.</param>
    /// <param name="driveLetter">Optional drive letter used for legacy drive-letter mode.</param>
    /// <param name="networkLocationName">Optional network location name for network-location mode.</param>
    /// <param name="progress">Optional progress reporter that receives status messages.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>An UnmountAndUnmapResult indicating success (including disk index and optional drive letter) or failure (including disk index, an error message, and the failed operation identifier). The returned drive letter (if any) is only populated when the unmapping operation succeeds. If unmapping fails but unmounting succeeds, the result will have Success = false with FailedStep = "unmap" to indicate the workflow did not complete successfully.</returns>
    public async Task<UnmountAndUnmapResult> UnmountAndUnmapAsync(
        int diskIndex,
        WindowsAccessMode accessMode,
        char? driveLetter = null,
        string? networkLocationName = null,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        // Validate parameters
        if (diskIndex < 0)
        {
            var validationFailure = UnmountAndUnmapResult.CreateFailure(diskIndex, "Disk index must be non-negative", "validation");
            validationFailure.AccessMode = accessMode;
            return validationFailure;
        }

        if (accessMode == WindowsAccessMode.DriveLetterLegacy &&
            (!driveLetter.HasValue || !char.IsLetter(driveLetter.Value)))
        {
            var validationFailure = UnmountAndUnmapResult.CreateFailure(
                diskIndex,
                "Drive letter is required and must be a valid letter (A-Z) in legacy mode.",
                "validation");
            validationFailure.AccessMode = accessMode;
            return validationFailure;
        }

        progress?.Report($"Starting unmount operation for disk {diskIndex}...");

        // Step 1: Unmap drive letter (if provided)
        string? unmappedDriveLetter = null;
        bool unmappingFailed = false;
        string? unmappingError = null;
        
        if (accessMode != WindowsAccessMode.None)
        {
            progress?.Report(accessMode switch
            {
                WindowsAccessMode.NetworkLocation => "Removing Explorer Network Location...",
                WindowsAccessMode.DriveLetterLegacy => $"Unmapping drive letter {driveLetter}:...",
                _ => "Removing Windows access..."
            });

            Result unmappingResult = await _windowsAccessService.RemoveAccessAsync(
                new WindowsAccessInfo
                {
                    AccessMode = accessMode,
                    AccessPathUNC = string.Empty,
                    DriveLetter = driveLetter,
                    NetworkLocationName = networkLocationName
                },
                cancellationToken);

            if (unmappingResult.IsFailure)
            {
                progress?.Report($"Windows access removal failed: {unmappingResult.ErrorMessage}");
                unmappingFailed = true;
                unmappingError = unmappingResult.ErrorMessage;
            }
            else
            {
                if (driveLetter.HasValue)
                {
                    unmappedDriveLetter = driveLetter.Value.ToString();
                }

                progress?.Report(accessMode switch
                {
                    WindowsAccessMode.NetworkLocation => $"Network Location '{networkLocationName}' removed successfully.",
                    WindowsAccessMode.DriveLetterLegacy => $"Drive letter {driveLetter}: unmapped successfully.",
                    _ => "Windows access removed."
                });
            }
        }

        // Step 2: Unmount from WSL
        progress?.Report("Unmounting disk from WSL...");

        var unmountResult = await _mountScriptService.ExecuteUnmountScriptAsync(diskIndex, cancellationToken);

        if (!unmountResult.Success)
        {
            if (IsAlreadyDetachedUnmountError(unmountResult.ErrorMessage))
            {
                _logger?.LogInformation(
                    "Unmount reported already-detached state for disk {DiskIndex}. Treating as success. Message: {Message}",
                    diskIndex,
                    unmountResult.ErrorMessage);
                progress?.Report("Disk is already detached from WSL. Continuing cleanup.");
            }
            else
            {
                progress?.Report($"Unmount failed: {unmountResult.ErrorMessage}");
                var failureResult = UnmountAndUnmapResult.CreateFailure(
                    diskIndex,
                    unmountResult.ErrorMessage ?? "Unknown error during unmount",
                    "unmount");
                failureResult.AccessMode = accessMode;
                failureResult.DriveLetter = driveLetter;
                failureResult.NetworkLocationName = networkLocationName;

                // Log failure to history (best-effort)
                await LogToHistoryAsync(failureResult, cancellationToken);

                return failureResult;
            }
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
                unmappingError ?? "Windows access removal failed",
                "unmap");
            result.AccessMode = accessMode;
            result.DriveLetter = driveLetter;
            result.NetworkLocationName = networkLocationName;
        }
        else
        {
            // Complete success
            result = UnmountAndUnmapResult.CreateSuccess(
                diskIndex,
                accessMode,
                string.IsNullOrEmpty(unmappedDriveLetter) ? null : unmappedDriveLetter[0],
                networkLocationName);
        }

        // Log to history (best-effort)
        await LogToHistoryAsync(result, cancellationToken);

        return result;
    }

    private static bool IsAlreadyDetachedUnmountError(string? errorMessage)
    {
        if (string.IsNullOrWhiteSpace(errorMessage))
        {
            return false;
        }

        return errorMessage.Contains("not currently attached", StringComparison.OrdinalIgnoreCase)
            || errorMessage.Contains("not mounted", StringComparison.OrdinalIgnoreCase)
            || errorMessage.Contains("ERROR_FILE_NOT_FOUND", StringComparison.OrdinalIgnoreCase)
            || errorMessage.Contains("The system cannot find the file specified", StringComparison.OrdinalIgnoreCase)
            || errorMessage.Contains("Wsl/Service/DetachDisk", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Logs the operation result to history service in a best-effort manner.
    /// Exceptions from the history service are caught and logged but do not fail the operation.
    /// </summary>
    /// <param name="result">The result to log to history.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    private async Task LogToHistoryAsync(UnmountAndUnmapResult result, CancellationToken cancellationToken = default)
    {
        if (_historyService == null) return;

        try
        {
            var historyEntry = MountHistoryEntry.FromUnmountResult(result);
            await _historyService.AddEntryAsync(historyEntry, cancellationToken);
        }
        catch (Exception ex)
        {
            // History logging is best-effort - don't fail the operation
            _logger?.LogWarning(ex, "Failed to log unmount operation to history for disk {DiskIndex}", result.DiskIndex);
        }
    }
}
