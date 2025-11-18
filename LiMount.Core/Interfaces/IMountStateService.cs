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
    /// <returns>Read-only list of active mounts.</returns>
    Task<IReadOnlyList<ActiveMount>> GetActiveMountsAsync();

    /// <summary>
    /// Registers a new mount operation.
    /// </summary>
    /// <param name="mount">The active mount to register.</param>
    Task RegisterMountAsync(ActiveMount mount);

    /// <summary>
    /// Unregisters a mount operation by disk index.
    /// </summary>
    /// <param name="diskIndex">The disk index to unregister.</param>
    Task UnregisterMountAsync(int diskIndex);

    /// <summary>
    /// Gets the active mount for a specific disk, if any.
    /// </summary>
    /// <param name="diskIndex">The disk index to query.</param>
    /// <returns>The active mount or null if not found.</returns>
    Task<ActiveMount?> GetMountForDiskAsync(int diskIndex);

    /// <summary>
    /// Gets the active mount for a specific drive letter, if any.
    /// </summary>
    /// <param name="driveLetter">The drive letter to query.</param>
    /// <returns>The active mount or null if not found.</returns>
    Task<ActiveMount?> GetMountForDriveLetterAsync(char driveLetter);

    /// <summary>
    /// Checks if a specific disk is currently mounted.
    /// </summary>
    /// <param name="diskIndex">The disk index to check.</param>
    /// <returns>True if the disk is mounted, false otherwise.</returns>
    Task<bool> IsDiskMountedAsync(int diskIndex);

    /// <summary>
    /// Checks if a specific drive letter is currently in use by a mount.
    /// </summary>
    /// <param name="driveLetter">The drive letter to check.</param>
    /// <returns>True if the drive letter is in use, false otherwise.</returns>
    Task<bool> IsDriveLetterInUseAsync(char driveLetter);

    /// <summary>
    /// Verifies and reconciles mount state with actual WSL/Windows state.
    /// Should be called on application startup to detect orphaned mounts.
    /// </summary>
    /// <returns>List of mounts that were found to be stale/orphaned.</returns>
    Task<IReadOnlyList<ActiveMount>> ReconcileMountStateAsync();

    /// <summary>
    /// Clears all mount state (for testing or cleanup purposes).
    /// </summary>
    Task ClearAllMountsAsync();
}
