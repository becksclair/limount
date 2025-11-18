using LiMount.Core.Models;

namespace LiMount.Core.Interfaces;

/// <summary>
/// Interface for disk and partition enumeration services.
/// </summary>
public interface IDiskEnumerationService
{
    /// <summary>
    /// Enumerates all physical disk drives on the system and includes their partition details.
    /// </summary>
    /// <returns>A read-only list of <see cref="DiskInfo"/> objects representing each physical disk and its partitions.</returns>
    IReadOnlyList<DiskInfo> GetDisks();

    /// <summary>
    /// Retrieves disks that are candidates for mounting on the system.
    /// </summary>
    /// <remarks>
    /// Candidate disks typically exclude system and boot drives and favor disks that contain at least one partition likely intended for Linux.
    /// </remarks>
    /// <returns>A read-only list of DiskInfo objects representing disks suitable for mounting (excludes system/boot disks and prefers disks with likely Linux partitions).</returns>
    IReadOnlyList<DiskInfo> GetCandidateDisks();
}