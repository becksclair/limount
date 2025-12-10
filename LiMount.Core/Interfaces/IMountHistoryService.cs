using LiMount.Core.Models;

namespace LiMount.Core.Interfaces;

/// <summary>
/// Service for tracking and persisting mount/unmount operation history.
/// </summary>
public interface IMountHistoryService
{
    /// <summary>
    /// Adds a history entry for a mount or unmount operation.
    /// </summary>
    /// <param name="entry">The history entry to add.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task AddEntryAsync(MountHistoryEntry entry, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all history entries, ordered by timestamp (newest first).
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>List of all history entries.</returns>
    Task<IReadOnlyList<MountHistoryEntry>> GetHistoryAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears all history entries.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task ClearHistoryAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the most recent mount operation for a specific disk, if any.
    /// </summary>
    /// <param name="diskIndex">The disk index to search for.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The most recent mount entry for the disk, or null if not found.</returns>
    Task<MountHistoryEntry?> GetLastMountForDiskAsync(int diskIndex, CancellationToken cancellationToken = default);
}
