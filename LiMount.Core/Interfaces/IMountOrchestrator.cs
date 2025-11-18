using LiMount.Core.Models;

namespace LiMount.Core.Interfaces;

/// <summary>
/// Orchestrates the complete mount workflow: WSL mounting + drive letter mapping.
/// </summary>
public interface IMountOrchestrator
{
    /// <summary>
    /// Mounts a disk partition in WSL and maps it to a Windows drive letter.
    /// </summary>
    /// <param name="diskIndex">Physical disk index</param>
    /// <param name="partition">Partition number</param>
    /// <param name="driveLetter">Target drive letter</param>
    /// <param name="fsType">Filesystem type</param>
    /// <param name="distroName">Optional WSL distribution name</param>
    /// <param name="progress">Optional progress callback</param>
    /// <summary>
        /// Orchestrates mounting a disk partition inside WSL and mapping it to a Windows drive letter.
        /// </summary>
        /// <param name="diskIndex">Physical disk index to operate on.</param>
        /// <param name="partition">Partition number on the specified disk.</param>
        /// <param name="driveLetter">Target Windows drive letter to assign.</param>
        /// <param name="fsType">Filesystem type to use for mounting; defaults to "ext4".</param>
        /// <param name="distroName">Optional WSL distribution name to perform the mount in; if null, the default distribution is used.</param>
        /// <param name="progress">Optional progress reporter that receives status updates as strings.</param>
        /// <returns>A MountAndMapResult representing the combined outcome of the WSL mount and Windows drive-letter mapping.</returns>
    Task<MountAndMapResult> MountAndMapAsync(
        int diskIndex,
        int partition,
        char driveLetter,
        string fsType = "ext4",
        string? distroName = null,
        IProgress<string>? progress = null);
}