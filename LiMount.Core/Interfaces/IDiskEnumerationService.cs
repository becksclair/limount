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
    /// <summary>
/// Enumerates all physical disk drives on the system and their partition details.
/// </summary>
/// <returns>A read-only list of <see cref="DiskInfo"/> objects representing each physical disk drive, including partition information.</returns>
    IReadOnlyList<DiskInfo> GetDisks();

    /// <summary>
    /// Gets only disks that are candidates for mounting (non-system, non-boot).
    /// Prefers disks with at least one likely Linux partition.
    /// </summary>
    /// <summary>
/// Enumerates physical disks that are candidates for mounting, excluding system and boot disks and favoring disks that contain at least one likely Linux partition.
/// </summary>
/// <returns>A read-only list of DiskInfo objects representing candidate disks suitable for mounting.</returns>
    IReadOnlyList<DiskInfo> GetCandidateDisks();
}