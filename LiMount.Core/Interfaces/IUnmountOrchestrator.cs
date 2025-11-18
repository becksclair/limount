using LiMount.Core.Models;

namespace LiMount.Core.Interfaces;

/// <summary>
/// Orchestrates the complete unmount workflow: drive letter unmapping + WSL unmounting.
/// </summary>
public interface IUnmountOrchestrator
{
    /// <summary>
    /// Unmaps a drive letter and unmounts the disk from WSL.
    /// </summary>
    /// <param name="diskIndex">Physical disk index to unmount</param>
    /// <param name="driveLetter">Drive letter to unmap (optional if not mapped)</param>
    /// <param name="progress">Optional progress callback</param>
    /// <returns>Combined result of unmapping and unmount operations</returns>
    Task<UnmountAndUnmapResult> UnmountAndUnmapAsync(
        int diskIndex,
        char? driveLetter = null,
        IProgress<string>? progress = null);
}
