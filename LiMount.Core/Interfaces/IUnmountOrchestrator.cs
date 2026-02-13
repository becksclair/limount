using LiMount.Core.Models;

namespace LiMount.Core.Interfaces;

/// <summary>
/// Orchestrates the complete unmount workflow: Windows access removal + WSL unmounting.
/// </summary>
public interface IUnmountOrchestrator
{
    /// <summary>
    /// Removes Windows access and unmounts the disk from WSL.
    /// </summary>
    /// <param name="diskIndex">Physical disk index to unmount.</param>
    /// <param name="accessMode">Windows access mode to remove.</param>
    /// <param name="driveLetter">Optional drive letter when using legacy drive mapping.</param>
    /// <param name="networkLocationName">Optional network location name when using network location mode.</param>
    /// <param name="progress">Optional progress reporter that receives status messages.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>An <see cref="UnmountAndUnmapResult"/> describing the outcomes of the unmapping and unmount operations.</returns>
    Task<UnmountAndUnmapResult> UnmountAndUnmapAsync(
        int diskIndex,
        WindowsAccessMode accessMode,
        char? driveLetter = null,
        string? networkLocationName = null,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);
}
