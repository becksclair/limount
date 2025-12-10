using System.Runtime.Versioning;
using Microsoft.Extensions.Options;
using LiMount.Core.Interfaces;
using LiMount.Core.Models;
using LiMount.Core.Configuration;

namespace LiMount.Core.Services;

/// <summary>
/// Orchestrates the complete mount workflow: WSL mounting + drive letter mapping.
/// Implements retry logic and detailed progress reporting.
/// </summary>
[SupportedOSPlatform("windows")]
public class MountOrchestrator : IMountOrchestrator
{
    private readonly IMountScriptService _mountScriptService;
    private readonly IDriveMappingService _driveMappingService;
    private readonly IMountHistoryService? _historyService;
    private readonly IMountStateService? _mountStateService;
    private readonly MountOperationsConfig _config;
    private readonly int _validatedUncRetries;
    private readonly int _validatedUncDelayMs;
    private readonly int _rollbackTimeoutMs;
    private const double UncJitterRatio = 0.2; // ±20% bounded jitter for UNC waits
    private const int UncBackoffCapMultiplier = 8; // cap at 8x base delay

    /// <summary>
    /// Initializes a new instance of <see cref="MountOrchestrator"/> using the provided services.
    /// </summary>
    /// <param name="mountScriptService">Service for executing mount/unmount scripts.</param>
    /// <param name="driveMappingService">Service for drive letter mapping operations.</param>
    /// <param name="config">Configuration for mount operations.</param>
    /// <param name="historyService">Optional history service for tracking mount operations.</param>
    /// <exception cref="ArgumentNullException">Thrown when required parameters are null.</exception>
    public MountOrchestrator(
        IMountScriptService mountScriptService,
        IDriveMappingService driveMappingService,
        IOptions<LiMountConfiguration> config,
        IMountHistoryService? historyService = null,
        IMountStateService? mountStateService = null)
    {
        ArgumentNullException.ThrowIfNull(mountScriptService);
        ArgumentNullException.ThrowIfNull(driveMappingService);
        ArgumentNullException.ThrowIfNull(config);
        _mountScriptService = mountScriptService;
        _driveMappingService = driveMappingService;
        _config = config.Value.MountOperations;
        _historyService = historyService;
        _mountStateService = mountStateService;

        // Validate and clamp UNC retry configuration to prevent unsafe values
        _validatedUncRetries = Math.Max(0, Math.Min(100, _config.UncAccessibilityRetries));
        _validatedUncDelayMs = Math.Max(10, Math.Min(5000, _config.UncAccessibilityDelayMs));
        _rollbackTimeoutMs = Math.Max(500, Math.Min(10000, _config.WslCommandTimeoutMs));
    }

    /// <summary>
    /// Orchestrates mounting a disk inside WSL and mapping a Windows drive letter to the resulting UNC share.
    /// </summary>
    /// <param name="diskIndex">Zero-based index of the disk to mount. Must be non-negative.</param>
    /// <param name="partition">Partition number on the disk to mount. Must be greater than 0.</param>
    /// <param name="driveLetter">Drive letter to assign for the Windows mapping. Must be a valid letter (A-Z).</param>
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
        char driveLetter,
        string fsType = "ext4",
        string? distroName = null,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        // Validate parameters
        if (diskIndex < 0)
        {
            return MountAndMapResult.CreateFailure(diskIndex, partition, "Disk index must be non-negative", "validation");
        }

        if (partition < 1)
        {
            return MountAndMapResult.CreateFailure(diskIndex, partition, "Partition number must be greater than 0", "validation");
        }

        if (!char.IsLetter(driveLetter))
        {
            return MountAndMapResult.CreateFailure(diskIndex, partition, "Drive letter must be a valid letter (A-Z)", "validation");
        }

        if (string.IsNullOrWhiteSpace(fsType))
        {
            return MountAndMapResult.CreateFailure(diskIndex, partition, "Filesystem type cannot be empty", "validation");
        }

        progress?.Report($"Starting mount operation for disk {diskIndex} partition {partition}...");

        ActiveMount? existingMountForDisk = null;
        ActiveMount? existingMountForDriveLetter = null;
        if (_mountStateService != null)
        {
            try
            {
                existingMountForDisk = await _mountStateService.GetMountForDiskAsync(diskIndex, cancellationToken);
                existingMountForDriveLetter = await _mountStateService.GetMountForDriveLetterAsync(driveLetter, cancellationToken);
            }
            catch
            {
                // Best-effort: ignore mount state lookup failures to avoid blocking the workflow.
            }
        }

        // Preflight drive letter usage before starting any side effects
        if (existingMountForDriveLetter != null)
        {
            if (IsSameTarget(existingMountForDriveLetter, diskIndex, partition, null, null))
            {
                // Idempotent: already mapped to the same target, short-circuit as success
                progress?.Report($"Drive letter {driveLetter}: already mapped to requested mount. Returning existing mapping.");
                return MountAndMapResult.CreateSuccess(
                    existingMountForDriveLetter.DiskIndex,
                    existingMountForDriveLetter.PartitionNumber,
                    existingMountForDriveLetter.DriveLetter,
                    existingMountForDriveLetter.DistroName,
                    existingMountForDriveLetter.MountPathLinux,
                    existingMountForDriveLetter.MountPathUNC);
            }

            // Conflict: drive letter is in use by a different target
            return MountAndMapResult.CreateFailure(
                diskIndex,
                partition,
                $"Drive letter {driveLetter} is already mapped to a different target (disk {existingMountForDriveLetter.DiskIndex} partition {existingMountForDriveLetter.PartitionNumber}).",
                "validation");
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
            return MountAndMapResult.CreateFailure(
                diskIndex,
                partition,
                "Operation canceled during mount.",
                "mount");
        }

        if (!mountResult.Success)
        {
            progress?.Report($"Mount failed: {mountResult.ErrorMessage}");
            var failureResult = MountAndMapResult.CreateFailure(
                diskIndex,
                partition,
                mountResult.ErrorMessage ?? "Unknown error during mount",
                "mount");

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

            var cleanupNote = await TryRollbackUnmountAsync(
                diskIndex,
                partition,
                mountResult,
                existingMountForDisk,
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

            var cleanupNote = await TryRollbackUnmountAsync(
                diskIndex,
                partition,
                mountResult,
                existingMountForDisk,
                progress,
                CancellationToken.None);
            failureResult.ErrorMessage = AppendCleanupNote(failureResult.ErrorMessage, cleanupNote);

            await LogToHistoryAsync(failureResult, cancellationToken);
            return failureResult;
        }

        // Step 2: Map drive letter
        progress?.Report($"Mapping drive letter {driveLetter}:...");

        MappingResult mappingResult;
        try
        {
            mappingResult = await _driveMappingService.ExecuteMappingScriptAsync(
                driveLetter,
                uncPath,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            progress?.Report("Drive mapping canceled; attempting rollback.");
            var failureResult = MountAndMapResult.CreateFailure(
                diskIndex,
                partition,
                "Operation canceled during drive mapping.",
                "map");

            var cleanupNote = await TryRollbackUnmountAsync(
                diskIndex,
                partition,
                mountResult,
                existingMountForDisk,
                progress,
                CancellationToken.None);
            failureResult.ErrorMessage = AppendCleanupNote(failureResult.ErrorMessage, cleanupNote);

            await LogToHistoryAsync(failureResult, cancellationToken);
            return failureResult;
        }

        if (!mappingResult.Success)
        {
            progress?.Report($"Drive mapping failed: {mappingResult.ErrorMessage}");
            var failureResult = MountAndMapResult.CreateFailure(
                diskIndex,
                partition,
                mappingResult.ErrorMessage ?? "Unknown error during mapping",
                "map");

            var cleanupNote = await TryRollbackUnmountAsync(
                diskIndex,
                partition,
                mountResult,
                existingMountForDisk,
                progress,
                CancellationToken.None);
            failureResult.ErrorMessage = AppendCleanupNote(failureResult.ErrorMessage, cleanupNote);

            await LogToHistoryAsync(failureResult, cancellationToken);

            return failureResult;
        }

        progress?.Report($"Successfully mapped as {driveLetter}:");

        // Create success result
        var result = MountAndMapResult.CreateSuccess(
            diskIndex,
            partition,
            driveLetter,
            mountResult.DistroName ?? "Unknown",
            mountResult.MountPathLinux ?? string.Empty,
            mountResult.MountPathUNC ?? string.Empty);

        // Register state when mapping succeeds
        if (_mountStateService != null)
        {
            try
            {
                var activeMount = CreateActiveMountFromResult(result);
                await _mountStateService.RegisterMountAsync(activeMount, cancellationToken);
            }
            catch
            {
                // Best effort: state persistence should not fail the operation
            }
        }

        await LogToHistoryAsync(result, cancellationToken);

        return result;
    }

    private async Task<string?> TryRollbackUnmountAsync(
        int diskIndex,
        int partition,
        MountResult mountResult,
        ActiveMount? existingMountForDisk,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        if (!ShouldAttemptRollback(existingMountForDisk, partition, mountResult))
        {
            return "Cleanup skipped because an existing mount for this disk does not match the current operation.";
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
                    await _mountStateService.UnregisterMountAsync(diskIndex, cancellationToken);
                }
                catch
                {
                    // Best effort: state cleanup failure should not mask original failure
                }
            }

            return null;
        }

        progress?.Report($"Rollback unmount failed: {unmountResult.ErrorMessage}");
        return unmountResult.ErrorMessage ?? "Rollback unmount failed for an unknown reason.";
    }

    private static bool ShouldAttemptRollback(ActiveMount? existingMountForDisk, int partition, MountResult mountResult)
    {
        if (existingMountForDisk == null)
        {
            return true;
        }

        if (existingMountForDisk.PartitionNumber != partition)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(existingMountForDisk.MountPathUNC) &&
            !string.IsNullOrWhiteSpace(mountResult.MountPathUNC))
        {
            return string.Equals(
                existingMountForDisk.MountPathUNC,
                mountResult.MountPathUNC,
                StringComparison.OrdinalIgnoreCase);
        }

        if (!string.IsNullOrWhiteSpace(existingMountForDisk.MountPathLinux) &&
            !string.IsNullOrWhiteSpace(mountResult.MountPathLinux))
        {
            return string.Equals(
                existingMountForDisk.MountPathLinux,
                mountResult.MountPathLinux,
                StringComparison.OrdinalIgnoreCase);
        }

        // Fallback: allow rollback if disk/partition matches but paths are missing/empty.
        return true;
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
            return Directory.Exists(uncPath);
        }

        var delayMs = _validatedUncDelayMs;

        for (int attempt = 0; attempt < attemptCount; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Directory.Exists(uncPath))
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
            DriveLetter = result.DriveLetter ?? default,
            DistroName = result.DistroName ?? string.Empty,
            MountPathLinux = result.MountPathLinux ?? string.Empty,
            MountPathUNC = result.MountPathUNC ?? string.Empty,
            MountedAt = DateTime.Now,
            IsVerified = true,
            LastVerified = DateTime.Now
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
}