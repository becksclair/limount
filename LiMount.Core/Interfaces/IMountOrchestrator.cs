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
    /// <returns>Combined result of mount and mapping operations</returns>
    Task<MountAndMapResult> MountAndMapAsync(
        int diskIndex,
        int partition,
        char driveLetter,
        string fsType = "ext4",
        string? distroName = null,
        IProgress<string>? progress = null);
}
