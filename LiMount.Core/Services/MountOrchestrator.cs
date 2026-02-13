using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using LiMount.Core.Interfaces;
using LiMount.Core.Models;
using LiMount.Core.Configuration;
using LiMount.Core.Results;

namespace LiMount.Core.Services;

/// <summary>
/// Orchestrates the complete mount workflow: WSL mounting + drive letter mapping.
/// Implements retry logic and detailed progress reporting.
/// </summary>
[SupportedOSPlatform("windows")]
public class MountOrchestrator : IMountOrchestrator
{
    private readonly IMountScriptService _mountScriptService;
    private readonly IWindowsAccessService _windowsAccessService;
    private readonly IMountHistoryService? _historyService;
    private readonly IMountStateService? _mountStateService;
    private readonly ILogger<MountOrchestrator>? _logger;
    private readonly MountOperationsConfig _config;
    private readonly int _validatedUncRetries;
    private readonly int _validatedUncDelayMs;
    private readonly int _uncExistenceTimeoutMs;
    private readonly int _rollbackTimeoutMs;
    private const double UncJitterRatio = 0.2; // ±20% bounded jitter for UNC waits
    private const int UncBackoffCapMultiplier = 8; // cap at 8x base delay

    /// <summary>
    /// Initializes a new instance of <see cref="MountOrchestrator"/> using the provided services.
    /// </summary>
    /// <param name="mountScriptService">Service for executing mount/unmount scripts.</param>
    /// <param name="windowsAccessService">Service for Windows access integration operations.</param>
    /// <param name="config">Configuration for mount operations.</param>
    /// <param name="historyService">Optional history service for tracking mount operations.</param>
    /// <param name="mountStateService">Optional state service for tracking active mounts.</param>
    /// <param name="logger">Optional logger for diagnostic information.</param>
    /// <exception cref="ArgumentNullException">Thrown when required parameters are null.</exception>
    public MountOrchestrator(
        IMountScriptService mountScriptService,
        IWindowsAccessService windowsAccessService,
        IOptions<LiMountConfiguration> config,
        IMountHistoryService? historyService = null,
        IMountStateService? mountStateService = null,
        ILogger<MountOrchestrator>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(mountScriptService);
        ArgumentNullException.ThrowIfNull(windowsAccessService);
        ArgumentNullException.ThrowIfNull(config);
        _mountScriptService = mountScriptService;
        _windowsAccessService = windowsAccessService;
        _config = config.Value.MountOperations;
        _historyService = historyService;
        _mountStateService = mountStateService;
        _logger = logger;

        // Validate and clamp UNC retry configuration to prevent unsafe values
        _validatedUncRetries = Math.Max(0, Math.Min(100, _config.UncAccessibilityRetries));
        _validatedUncDelayMs = Math.Max(10, Math.Min(5000, _config.UncAccessibilityDelayMs));
        _uncExistenceTimeoutMs = Math.Max(100, Math.Min(30000, _config.UncExistenceCheckTimeoutMs));
        _rollbackTimeoutMs = Math.Max(500, Math.Min(10000, _config.RollbackTimeoutMs));
    }

    /// <summary>
    /// Orchestrates mounting a disk inside WSL and creating the configured Windows access surface.
    /// </summary>
    /// <param name="diskIndex">Zero-based index of the disk to mount. Must be non-negative.</param>
    /// <param name="partition">Partition number on the disk to mount. Must be greater than 0.</param>
    /// <param name="accessMode">Windows access mode to apply after mount.</param>
    /// <param name="driveLetter">Optional drive letter for legacy drive-letter mode.</param>
    /// <param name="fsType">Filesystem type to use for mounting (e.g., "ext4"). Cannot be empty or null.</param>
    /// <param name="distroName">Optional WSL distribution name to perform the mount under; if null, a default or detection may be used.</param>
    /// <param name="progress">Optional progress reporter that receives status messages during the operation.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>
    /// A MountAndMapResult describing the outcome. On success the result contains the disk and partition identifiers, the mapped drive letter, the distribution name, the Linux mount path, and the UNC path. On failure the result contains an error message and the stage that failed ("validation", "mount", or "map").
    /// </returns>
    public async Task<MountAndMapResult> MountAndMapAsync(
        int diskIndex,
        int partition,
        WindowsAccessMode accessMode,
        char? driveLetter = null,
        string fsType = "ext4",
        string? distroName = null,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        // Validate parameters
        if (diskIndex < 0)
        {
            var validationFailure = MountAndMapResult.CreateFailure(diskIndex, partition, "Disk index must be non-negative", "validation");
            validationFailure.AccessMode = accessMode;
            return validationFailure;
        }

        if (partition < 1)
        {
            var validationFailure = MountAndMapResult.CreateFailure(diskIndex, partition, "Partition number must be greater than 0", "validation");
            validationFailure.AccessMode = accessMode;
            return validationFailure;
        }

        if (accessMode == WindowsAccessMode.DriveLetterLegacy &&
            (!driveLetter.HasValue || !char.IsLetter(driveLetter.Value)))
        {
            var validationFailure = MountAndMapResult.CreateFailure(
                diskIndex,
                partition,
                "Drive letter must be a valid letter (A-Z) in legacy drive-letter mode.",
                "validation");
            validationFailure.AccessMode = accessMode;
            return validationFailure;
        }

        if (string.IsNullOrWhiteSpace(fsType))
        {
            var validationFailure = MountAndMapResult.CreateFailure(diskIndex, partition, "Filesystem type cannot be empty", "validation");
            validationFailure.AccessMode = accessMode;
            return validationFailure;
        }

        progress?.Report($"Starting mount operation for disk {diskIndex} partition {partition}...");

        ActiveMount? existingMountForPartition = null;
        ActiveMount? existingMountForDriveLetter = null;
        if (_mountStateService != null)
        {
            try
            {
                existingMountForPartition = await _mountStateService.GetMountForDiskPartitionAsync(diskIndex, partition, cancellationToken);
                if (accessMode == WindowsAccessMode.DriveLetterLegacy && driveLetter.HasValue)
                {
                    existingMountForDriveLetter = await _mountStateService.GetMountForDriveLetterAsync(driveLetter.Value, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                throw; // Let cancellation propagate
            }
            catch (Exception ex)
            {
                // Best-effort: mount state lookup failure should not block the workflow
                _logger?.LogWarning(ex, "Mount state lookup failed for disk {DiskIndex}; proceeding without conflict detection", diskIndex);
            }
        }

        // Preflight drive letter usage before starting any side effects (legacy mode only)
        if (existingMountForDriveLetter != null)
        {
            if (IsSameTarget(existingMountForDriveLetter, diskIndex, partition, null, null))
            {
                // Idempotent: already mapped to the same target, short-circuit as success
                progress?.Report($"Drive letter {driveLetter}: already mapped to requested mount. Returning existing mapping.");
                return MountAndMapResult.CreateSuccess(
                    existingMountForDriveLetter.DiskIndex,
                    existingMountForDriveLetter.PartitionNumber,
                    existingMountForDriveLetter.AccessMode,
                    existingMountForDriveLetter.DistroName,
                    existingMountForDriveLetter.MountPathLinux,
                    existingMountForDriveLetter.MountPathUNC,
                    existingMountForDriveLetter.DriveLetter,
                    existingMountForDriveLetter.NetworkLocationName);
            }

            // Conflict: drive letter is in use by a different target
            var validationFailure = MountAndMapResult.CreateFailure(
                diskIndex,
                partition,
                $"Drive letter {driveLetter} is already mapped to a different target (disk {existingMountForDriveLetter.DiskIndex} partition {existingMountForDriveLetter.PartitionNumber}).",
                "validation");
            validationFailure.AccessMode = accessMode;
            return validationFailure;
        }

        // Step 1: Mount disk in WSL
        progress?.Report("Mounting disk in WSL (this may take a moment)...");

        MountResult mountResult;
        try
        {
            mountResult = await _mountScriptService.ExecuteMountScriptAsync(
                diskIndex,
                partition,
                fsType,
                distroName,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            var canceledFailure = MountAndMapResult.CreateFailure(
                diskIndex,
                partition,
                "Operation canceled during mount.",
                "mount");
            canceledFailure.AccessMode = accessMode;
            return canceledFailure;
        }

        if (!mountResult.Success)
        {
            _logger?.LogWarning(
                "Mount failed for disk {DiskIndex} partition {Partition}. ErrorCode={ErrorCode}, ErrorHint={ErrorHint}, DmesgSummary={DmesgSummary}, Error={Error}",
                diskIndex,
                partition,
                mountResult.ErrorCode ?? "(none)",
                mountResult.ErrorHint ?? "(none)",
                mountResult.DmesgSummary ?? "(none)",
                mountResult.ErrorMessage ?? "(none)");

            var originalMountResult = mountResult;
            if (ShouldRetryWithAuto(fsType, mountResult))
            {
                progress?.Report("Mount failed with explicit filesystem type. Retrying once with auto-detection...");
                await TryBestEffortUnmountBeforeRetryAsync(diskIndex);

                try
                {
                    var retryResult = await _mountScriptService.ExecuteMountScriptAsync(
                        diskIndex,
                        partition,
                        "auto",
                        distroName,
                        cancellationToken);

                    if (retryResult.Success)
                    {
                        progress?.Report("Mount retry with auto-detection succeeded.");
                        mountResult = retryResult;
                    }
                    else
                    {
                        _logger?.LogWarning(
                            "Mount retry with auto-detection also failed. ErrorCode={ErrorCode}, ErrorHint={ErrorHint}, DmesgSummary={DmesgSummary}, Error={Error}",
                            retryResult.ErrorCode ?? "(none)",
                            retryResult.ErrorHint ?? "(none)",
                            retryResult.DmesgSummary ?? "(none)",
                            retryResult.ErrorMessage ?? "(none)");

                        mountResult = SelectDiagnosticResult(originalMountResult, retryResult);
                        mountResult.ErrorMessage = BuildRetryFailureDetails(fsType, originalMountResult, retryResult);
                    }
                }
                catch (OperationCanceledException)
                {
                    var retryCanceledFailure = MountAndMapResult.CreateFailure(
                        diskIndex,
                        partition,
                        "Operation canceled during mount retry.",
                        "mount");
                    retryCanceledFailure.AccessMode = accessMode;
                    return retryCanceledFailure;
                }
            }
        }

        if (!mountResult.Success)
        {
            var failureMessage = BuildUserFacingMountErrorMessage(mountResult);
            progress?.Report($"Mount failed: {failureMessage}");

            var failureResult = MountAndMapResult.CreateFailure(
                diskIndex,
                partition,
                failureMessage,
                "mount");
            failureResult.AccessMode = accessMode;

            await LogToHistoryAsync(failureResult, cancellationToken);
            return failureResult;
        }

        progress?.Report($"Disk mounted successfully at {mountResult.MountPathLinux}");

        var uncPath = mountResult.MountPathUNC;
        if (string.IsNullOrWhiteSpace(uncPath))
        {
            progress?.Report("Mount succeeded but no UNC path was returned; attempting rollback.");
            var failureResult = MountAndMapResult.CreateFailure(
                diskIndex,
                partition,
                "Mount succeeded but no UNC path was returned. Aborting before drive mapping.",
                "mount");
            failureResult.AccessMode = accessMode;

            var cleanupNote = await TryRollbackUnmountAsync(
                diskIndex,
                partition,
                mountResult,
                existingMountForPartition,
                progress,
                CancellationToken.None);
            failureResult.ErrorMessage = AppendCleanupNote(failureResult.ErrorMessage, cleanupNote);

            await LogToHistoryAsync(failureResult, cancellationToken);
            return failureResult;
        }

        progress?.Report("Verifying WSL share accessibility...");
        var uncAttemptCount = Math.Max(1, _validatedUncRetries);
        var uncAccessible = await WaitForUncAccessibilityAsync(uncPath, uncAttemptCount, cancellationToken);
        if (!uncAccessible)
        {
            var failureResult = MountAndMapResult.CreateFailure(
                diskIndex,
                partition,
                $"UNC path {uncPath} is not accessible after {uncAttemptCount} attempt(s).",
                "mount");
            failureResult.AccessMode = accessMode;

            var cleanupNote = await TryRollbackUnmountAsync(
                diskIndex,
                partition,
                mountResult,
                existingMountForPartition,
                progress,
                CancellationToken.None);
            failureResult.ErrorMessage = AppendCleanupNote(failureResult.ErrorMessage, cleanupNote);

            await LogToHistoryAsync(failureResult, cancellationToken);
            return failureResult;
        }

        // Step 2: Apply Windows access mode.
        progress?.Report(accessMode switch
        {
            WindowsAccessMode.NetworkLocation => "Creating Explorer Network Location...",
            WindowsAccessMode.DriveLetterLegacy => $"Mapping drive letter {driveLetter}:...",
            _ => "Skipping Windows integration (None mode)..."
        });

        Result<WindowsAccessInfo> windowsAccessResult;
        try
        {
            windowsAccessResult = await _windowsAccessService.CreateAccessAsync(
                new WindowsAccessRequest
                {
                    AccessMode = accessMode,
                    TargetUNC = uncPath,
                    DriveLetter = driveLetter,
                    DiskIndex = diskIndex,
                    PartitionNumber = partition
                },
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            progress?.Report("Windows access setup canceled; attempting rollback.");
            var failureResult = MountAndMapResult.CreateFailure(
                diskIndex,
                partition,
                "Operation canceled during Windows access setup.",
                "map");
            failureResult.AccessMode = accessMode;

            var cleanupNote = await TryRollbackUnmountAsync(
                diskIndex,
                partition,
                mountResult,
                existingMountForPartition,
                progress,
                CancellationToken.None);
            failureResult.ErrorMessage = AppendCleanupNote(failureResult.ErrorMessage, cleanupNote);

            await LogToHistoryAsync(failureResult, cancellationToken);
            return failureResult;
        }

        if (windowsAccessResult.IsFailure || windowsAccessResult.Value == null)
        {
            progress?.Report($"Windows access setup failed: {windowsAccessResult.ErrorMessage}");
            var failureResult = MountAndMapResult.CreateFailure(
                diskIndex,
                partition,
                windowsAccessResult.ErrorMessage ?? "Unknown error during Windows access setup",
                windowsAccessResult.FailedStep ?? "map");
            failureResult.AccessMode = accessMode;

            var cleanupNote = await TryRollbackUnmountAsync(
                diskIndex,
                partition,
                mountResult,
                existingMountForPartition,
                progress,
                CancellationToken.None);
            failureResult.ErrorMessage = AppendCleanupNote(failureResult.ErrorMessage, cleanupNote);

            await LogToHistoryAsync(failureResult, cancellationToken);
            return failureResult;
        }

        var windowsAccess = windowsAccessResult.Value;
        progress?.Report(windowsAccess.AccessMode switch
        {
            WindowsAccessMode.NetworkLocation => $"Network Location '{windowsAccess.NetworkLocationName}' created.",
            WindowsAccessMode.DriveLetterLegacy => $"Successfully mapped as {windowsAccess.DriveLetter}:",
            _ => "Mount completed without Windows integration."
        });

        // Create success result
        var result = MountAndMapResult.CreateSuccess(
            diskIndex,
            partition,
            windowsAccess.AccessMode,
            mountResult.DistroName ?? "Unknown",
            mountResult.MountPathLinux ?? string.Empty,
            mountResult.MountPathUNC ?? string.Empty,
            windowsAccess.DriveLetter,
            windowsAccess.NetworkLocationName);

        // Register state when mapping succeeds
        if (_mountStateService != null)
        {
            try
            {
                var activeMount = CreateActiveMountFromResult(result);
                await _mountStateService.RegisterMountAsync(activeMount, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // Don't let cancellation fail the operation - mount already succeeded
                _logger?.LogDebug("State persistence cancelled for disk {DiskIndex}; mount succeeded", diskIndex);
            }
            catch (Exception ex)
            {
                // Best effort: state persistence should not fail the operation, but log for diagnostics
                _logger?.LogWarning(ex, "Failed to persist mount state for disk {DiskIndex}; mount succeeded but state may be inconsistent", diskIndex);
            }
        }

        await LogToHistoryAsync(result, cancellationToken);

        return result;
    }

    private async Task<string?> TryRollbackUnmountAsync(
        int diskIndex,
        int partition,
        MountResult mountResult,
        ActiveMount? existingMountForPartition,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var rollbackSkipReason = GetRollbackSkipReason(existingMountForPartition, mountResult);
        if (!string.IsNullOrEmpty(rollbackSkipReason))
        {
            return rollbackSkipReason;
        }

        progress?.Report("Attempting rollback unmount...");

        using var rollbackCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        rollbackCts.CancelAfter(_rollbackTimeoutMs);

        UnmountResult unmountResult;
        try
        {
            unmountResult = await _mountScriptService.ExecuteUnmountScriptAsync(diskIndex, rollbackCts.Token);
        }
        catch (OperationCanceledException)
        {
            return "Rollback unmount canceled or timed out.";
        }

        if (unmountResult.Success)
        {
            progress?.Report("Rollback unmount completed.");

            if (_mountStateService != null)
            {
                try
                {
                    await _mountStateService.UnregisterMountAsync(diskIndex, partition, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    // Don't let cancellation fail rollback cleanup
                    _logger?.LogDebug("State cleanup cancelled during rollback for disk {DiskIndex}", diskIndex);
                }
                catch (Exception ex)
                {
                    // Best effort: state cleanup failure should not mask original failure
                    _logger?.LogWarning(ex, "Failed to unregister mount state during rollback for disk {DiskIndex}", diskIndex);
                }
            }

            return null;
        }

        progress?.Report($"Rollback unmount failed: {unmountResult.ErrorMessage}");
        return unmountResult.ErrorMessage ?? "Rollback unmount failed for an unknown reason.";
    }

    private static string? GetRollbackSkipReason(ActiveMount? existingMountForPartition, MountResult mountResult)
    {
        if (mountResult.AlreadyMounted)
        {
            return "Cleanup skipped because the target partition was already mounted before this operation.";
        }

        if (existingMountForPartition != null)
        {
            return "Cleanup skipped because an existing state entry for this disk/partition was present before this operation.";
        }

        // This operation appears to have mounted a previously unmounted partition.
        return null;
    }

    private string AppendCleanupNote(string? baseMessage, string? cleanupNote)
    {
        if (string.IsNullOrWhiteSpace(cleanupNote))
        {
            return baseMessage ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(baseMessage))
        {
            return cleanupNote;
        }

        return $"{baseMessage} (cleanup: {cleanupNote})";
    }

    private async Task LogToHistoryAsync(MountAndMapResult result, CancellationToken cancellationToken)
    {
        if (_historyService == null)
        {
            return;
        }

        var historyEntry = MountHistoryEntry.FromMountResult(result);
        await _historyService.AddEntryAsync(historyEntry, cancellationToken);
    }

    private async Task<bool> WaitForUncAccessibilityAsync(string uncPath, int attemptCount, CancellationToken cancellationToken)
    {
        if (attemptCount <= 0)
        {
            return await CheckDirectoryExistsWithTimeoutAsync(uncPath, _uncExistenceTimeoutMs, cancellationToken);
        }

        var delayMs = _validatedUncDelayMs;

        for (int attempt = 0; attempt < attemptCount; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Use timeout-protected check to avoid blocking on dead UNC paths
            if (await CheckDirectoryExistsWithTimeoutAsync(uncPath, _uncExistenceTimeoutMs, cancellationToken))
            {
                return true;
            }

            var isLastAttempt = attempt == attemptCount - 1;
            if (!isLastAttempt)
            {
                var jitteredDelay = ApplyJitter(delayMs);
                await Task.Delay(jitteredDelay, cancellationToken);
                delayMs = Math.Min(delayMs * 2, _validatedUncDelayMs * UncBackoffCapMultiplier);
            }
        }

        return false;
    }

    /// <summary>
    /// Checks if a directory exists with timeout protection.
    /// Directory.Exists() can block indefinitely on dead UNC paths (30-60s+).
    /// This method wraps the check with a timeout to prevent UI freezes.
    /// </summary>
    /// <remarks>
    /// Note: The underlying I/O operation cannot be truly cancelled. If timeout occurs,
    /// the background task continues running but we return false immediately.
    /// Uses Task.WhenAny pattern since Directory.Exists() is not cancellable.
    /// </remarks>
    private static async Task<bool> CheckDirectoryExistsWithTimeoutAsync(string path, int timeoutMs, CancellationToken cancellationToken)
    {
        // Check for external cancellation first
        cancellationToken.ThrowIfCancellationRequested();

        // Don't pass cancellation token to Task.Run - Directory.Exists() is not cancellable anyway
        var checkTask = Task.Run(() => Directory.Exists(path));
        var timeoutTask = Task.Delay(timeoutMs, CancellationToken.None);

        var completedTask = await Task.WhenAny(checkTask, timeoutTask);

        if (completedTask == checkTask)
        {
            // Completed before timeout - return result
            return await checkTask;
        }

        // Timeout occurred - path is likely inaccessible
        // Note: The checkTask continues running in background (unavoidable for blocking I/O)
        return false;
    }

    private static bool IsSameTarget(ActiveMount existing, int diskIndex, int partition, string? mountPathLinux, string? mountPathUnc)
    {
        if (existing.DiskIndex != diskIndex || existing.PartitionNumber != partition)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(mountPathUnc) && !string.IsNullOrWhiteSpace(existing.MountPathUNC))
        {
            return string.Equals(existing.MountPathUNC, mountPathUnc, StringComparison.OrdinalIgnoreCase);
        }

        if (!string.IsNullOrWhiteSpace(mountPathLinux) && !string.IsNullOrWhiteSpace(existing.MountPathLinux))
        {
            return string.Equals(existing.MountPathLinux, mountPathLinux, StringComparison.OrdinalIgnoreCase);
        }

        // If paths are missing, fall back to disk/partition match as the identity.
        return true;
    }

    private ActiveMount CreateActiveMountFromResult(MountAndMapResult result)
    {
        return new ActiveMount
        {
            Id = Guid.NewGuid().ToString(),
            DiskIndex = result.DiskIndex,
            PartitionNumber = result.Partition,
            AccessMode = result.AccessMode,
            DriveLetter = result.DriveLetter,
            NetworkLocationName = result.NetworkLocationName,
            DistroName = result.DistroName ?? string.Empty,
            MountPathLinux = result.MountPathLinux ?? string.Empty,
            MountPathUNC = result.MountPathUNC ?? string.Empty,
            MountedAt = DateTime.UtcNow,
            IsVerified = true,
            LastVerified = DateTime.UtcNow
        };
    }

    private int ApplyJitter(int baseDelayMs)
    {
        if (baseDelayMs <= 1)
        {
            return baseDelayMs;
        }

        var spread = (int)Math.Ceiling(baseDelayMs * UncJitterRatio);
        var min = Math.Max(1, baseDelayMs - spread);
        var max = baseDelayMs + spread + 1; // upper bound is exclusive
        return Random.Shared.Next(min, max);
    }

    private async Task TryBestEffortUnmountBeforeRetryAsync(int diskIndex)
    {
        try
        {
            await _mountScriptService.ExecuteUnmountScriptAsync(diskIndex, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Best-effort unmount before retry failed for disk {DiskIndex}", diskIndex);
        }
    }

    private static bool ShouldRetryWithAuto(string fsType, MountResult mountResult)
    {
        if (string.Equals(fsType, "auto", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.Equals(mountResult.ErrorCode, "XFS_UNSUPPORTED_FEATURES", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return string.Equals(mountResult.ErrorCode, "MOUNT_INVALID_ARGUMENT", StringComparison.OrdinalIgnoreCase) ||
               (mountResult.ErrorMessage?.Contains("Invalid argument", StringComparison.OrdinalIgnoreCase) ?? false);
    }

    private static MountResult SelectDiagnosticResult(MountResult firstAttempt, MountResult retryAttempt)
    {
        var firstScore = GetDiagnosticScore(firstAttempt);
        var retryScore = GetDiagnosticScore(retryAttempt);
        return retryScore >= firstScore ? retryAttempt : firstAttempt;
    }

    private static int GetDiagnosticScore(MountResult result)
    {
        var score = 0;
        if (!string.IsNullOrWhiteSpace(result.ErrorCode)) score += 4;
        if (!string.IsNullOrWhiteSpace(result.ErrorHint)) score += 3;
        if (!string.IsNullOrWhiteSpace(result.DmesgSummary)) score += 2;
        if (!string.IsNullOrWhiteSpace(result.ErrorMessage)) score += 1;
        return score;
    }

    private static string BuildRetryFailureDetails(string originalFsType, MountResult firstAttempt, MountResult retryAttempt)
    {
        var firstMessage = firstAttempt.ErrorMessage ?? "Unknown error";
        var retryMessage = retryAttempt.ErrorMessage ?? "Unknown error";
        return $"Initial mount with fsType '{originalFsType}' failed: {firstMessage}. Retry with fsType 'auto' also failed: {retryMessage}.";
    }

    private static string BuildUserFacingMountErrorMessage(MountResult result)
    {
        var details = result.ErrorMessage ?? "Unknown error during mount";

        if (!string.IsNullOrWhiteSpace(result.ErrorHint))
        {
            return $"{result.ErrorHint} Details: {details}";
        }

        return details;
    }
}
