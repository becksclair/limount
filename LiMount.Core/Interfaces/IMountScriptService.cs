using LiMount.Core.Models;

namespace LiMount.Core.Interfaces;

/// <summary>
/// Service interface for executing mount and unmount scripts.
/// Handles the WSL disk attachment and detachment operations.
/// </summary>
public interface IMountScriptService
{
    /// <summary>
    /// Executes the PowerShell script to mount a Linux partition on the specified physical disk.
    /// </summary>
    /// <param name="diskIndex">Index of the physical disk to mount.</param>
    /// <param name="partition">Partition number on the disk (1-based).</param>
    /// <param name="fsType">Filesystem type to mount (for example, "ext4" or "xfs").</param>
    /// <param name="distroName">Optional WSL distribution name to target; null to mount without a specific distribution.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A MountResult containing details and status of the mount operation.</returns>
    Task<MountResult> ExecuteMountScriptAsync(
        int diskIndex,
        int partition,
        string fsType,
        string? distroName = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes the elevated unmount script to unmount the specified physical disk.
    /// </summary>
    /// <param name="diskIndex">Physical disk index to unmount.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>An <see cref="UnmountResult"/> indicating the outcome of the unmount operation.</returns>
    Task<UnmountResult> ExecuteUnmountScriptAsync(int diskIndex, CancellationToken cancellationToken = default);
}
