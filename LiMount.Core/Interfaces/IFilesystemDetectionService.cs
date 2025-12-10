namespace LiMount.Core.Interfaces;

/// <summary>
/// Service interface for detecting filesystem types on disk partitions.
/// Uses WSL to query partition metadata.
/// </summary>
public interface IFilesystemDetectionService
{
    /// <summary>
    /// Detects the filesystem type of a partition using WSL.
    /// Temporarily attaches the disk with --bare, runs lsblk, then detaches.
    /// </summary>
    /// <param name="diskIndex">Physical disk index to query.</param>
    /// <param name="partitionNumber">Partition number (1-based).</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The detected filesystem type (e.g., "xfs", "ext4") or null if detection failed.</returns>
    Task<string?> DetectFilesystemTypeAsync(int diskIndex, int partitionNumber, CancellationToken cancellationToken = default);
}
