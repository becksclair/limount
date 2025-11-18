using LiMount.Core.Models;

namespace LiMount.Core.Interfaces;

/// <summary>
/// Interface for disk and partition enumeration services.
/// </summary>
public interface IDiskEnumerationService
{
    /// <summary>
    /// Enumerates all physical disk drives on the system.
    /// </summary>
    /// <returns>List of all disks with partition details</returns>
    IReadOnlyList<DiskInfo> GetDisks();

    /// <summary>
    /// Gets only disks that are candidates for mounting (non-system, non-boot).
    /// Prefers disks with at least one likely Linux partition.
    /// </summary>
    /// <returns>List of candidate disks suitable for mounting</returns>
    IReadOnlyList<DiskInfo> GetCandidateDisks();
}
