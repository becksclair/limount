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
    Task AddEntryAsync(MountHistoryEntry entry);

    /// <summary>
    /// Gets all history entries, ordered by timestamp (newest first).
    /// </summary>
    /// <returns>List of all history entries.</returns>
    Task<IReadOnlyList<MountHistoryEntry>> GetHistoryAsync();

    /// <summary>
    /// Clears all history entries.
    /// </summary>
    Task ClearHistoryAsync();

    /// <summary>
    /// Gets the most recent mount operation for a specific disk, if any.
    /// </summary>
    /// <param name="diskIndex">The disk index to search for.</param>
    /// <returns>The most recent mount entry for the disk, or null if not found.</returns>
    Task<MountHistoryEntry?> GetLastMountForDiskAsync(int diskIndex);
}
