using LiMount.Core.Models;

namespace LiMount.Core.Interfaces;

/// <summary>
/// Orchestrates the complete mount workflow: WSL mounting + Windows access integration.
/// </summary>
public interface IMountOrchestrator
{
    /// <summary>
    /// Orchestrates mounting a disk partition inside WSL and applying the selected Windows access mode.
    /// </summary>
    /// <param name="diskIndex">Physical disk index containing the target partition.</param>
    /// <param name="partition">Partition number on the specified disk to mount.</param>
    /// <param name="accessMode">Windows access mode to apply after mount.</param>
    /// <param name="driveLetter">Optional drive letter when <paramref name="accessMode"/> is legacy drive mapping.</param>
    /// <param name="fsType">Filesystem type to use for mounting (defaults to "ext4").</param>
    /// <param name="distroName">Optional WSL distribution name to perform the mount within; uses the default distribution if null.</param>
    /// <param name="progress">Optional progress reporter that receives human-readable status updates.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A MountAndMapResult representing the outcome of the mount and mapping operations.</returns>
    Task<MountAndMapResult> MountAndMapAsync(
        int diskIndex,
        int partition,
        WindowsAccessMode accessMode,
        char? driveLetter = null,
        string fsType = "ext4",
        string? distroName = null,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);
}
