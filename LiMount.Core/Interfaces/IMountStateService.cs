using LiMount.Core.Models;

namespace LiMount.Core.Interfaces;

/// <summary>
/// Service for managing and persisting the state of active mounts.
/// Provides central authority for tracking what disks are currently mounted.
/// </summary>
public interface IMountStateService
{
    /// <summary>
    /// Gets all currently active mounts.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>Read-only list of active mounts.</returns>
    Task<IReadOnlyList<ActiveMount>> GetActiveMountsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Registers a new mount operation.
    /// </summary>
    /// <param name="mount">The active mount to register.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task RegisterMountAsync(ActiveMount mount, CancellationToken cancellationToken = default);

    /// <summary>
    /// Unregisters a mount operation by disk index.
    /// </summary>
    /// <param name="diskIndex">The disk index to unregister.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    [Obsolete("Use UnregisterDiskAsync for disk-wide removal or UnregisterMountAsync(diskIndex, partition) for partition-scoped removal.")]
    Task UnregisterMountAsync(int diskIndex, CancellationToken cancellationToken = default);

    /// <summary>
    /// Unregisters a specific mounted partition.
    /// </summary>
    /// <param name="diskIndex">The disk index to unregister from.</param>
    /// <param name="partition">The partition number to unregister.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task UnregisterMountAsync(int diskIndex, int partition, CancellationToken cancellationToken = default);

    /// <summary>
    /// Unregisters all mounted partitions for a disk.
    /// </summary>
    /// <param name="diskIndex">The disk index to unregister.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task UnregisterDiskAsync(int diskIndex, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the active mount for a specific disk, if any.
    /// </summary>
    /// <param name="diskIndex">The disk index to query.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The active mount or null if not found.</returns>
    Task<ActiveMount?> GetMountForDiskAsync(int diskIndex, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the active mount for a specific disk partition, if any.
    /// </summary>
    /// <param name="diskIndex">The disk index to query.</param>
    /// <param name="partition">The partition number to query.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The active mount or null if not found.</returns>
    Task<ActiveMount?> GetMountForDiskPartitionAsync(int diskIndex, int partition, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all active mounts for a specific disk.
    /// </summary>
    /// <param name="diskIndex">The disk index to query.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>Read-only list of mounts for the disk.</returns>
    Task<IReadOnlyList<ActiveMount>> GetMountsForDiskAsync(int diskIndex, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the active mount for a specific drive letter, if any.
    /// </summary>
    /// <param name="driveLetter">The drive letter to query.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The active mount or null if not found.</returns>
    Task<ActiveMount?> GetMountForDriveLetterAsync(char driveLetter, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a specific disk is currently mounted.
    /// </summary>
    /// <param name="diskIndex">The disk index to check.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>True if the disk is mounted, false otherwise.</returns>
    Task<bool> IsDiskMountedAsync(int diskIndex, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a specific drive letter is currently in use by a mount.
    /// </summary>
    /// <param name="driveLetter">The drive letter to check.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>True if the drive letter is in use, false otherwise.</returns>
    Task<bool> IsDriveLetterInUseAsync(char driveLetter, CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies and reconciles mount state with actual WSL/Windows state.
    /// Should be called on application startup to detect orphaned mounts.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>List of mounts that were found to be stale/orphaned.</returns>
    Task<IReadOnlyList<ActiveMount>> ReconcileMountStateAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears all mount state (for testing or cleanup purposes).
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task ClearAllMountsAsync(CancellationToken cancellationToken = default);
}
