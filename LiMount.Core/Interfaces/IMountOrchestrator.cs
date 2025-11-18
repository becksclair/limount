using LiMount.Core.Models;

namespace LiMount.Core.Interfaces;

/// <summary>
/// Orchestrates the complete mount workflow: WSL mounting + drive letter mapping.
/// </summary>
public interface IMountOrchestrator
{
    /// <summary>
    /// Orchestrates mounting a disk partition inside WSL and mapping it to a Windows drive letter.
    /// </summary>
    /// <param name="diskIndex">Physical disk index containing the target partition.</param>
    /// <param name="partition">Partition number on the specified disk to mount.</param>
    /// <param name="driveLetter">Windows drive letter to assign to the mounted partition.</param>
    /// <param name="fsType">Filesystem type to use for mounting (defaults to "ext4").</param>
    /// <param name="distroName">Optional WSL distribution name to perform the mount within; uses the default distribution if null.</param>
    /// <param name="progress">Optional progress reporter that receives human-readable status updates.</param>
    /// <returns>A MountAndMapResult representing the outcome of the mount and mapping operations.</returns>
    Task<MountAndMapResult> MountAndMapAsync(
        int diskIndex,
        int partition,
        char driveLetter,
        string fsType = "ext4",
        string? distroName = null,
        IProgress<string>? progress = null);
}