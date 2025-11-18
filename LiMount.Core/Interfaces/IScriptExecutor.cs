using LiMount.Core.Models;

namespace LiMount.Core.Interfaces;

/// <summary>
/// Interface for executing PowerShell scripts and commands.
/// Abstracts the execution details to enable testing and different execution strategies.
/// </summary>
public interface IScriptExecutor
{
    /// <summary>
    /// Executes the PowerShell script to mount a Linux partition on the specified physical disk.
    /// </summary>
    /// <param name="diskIndex">Index of the physical disk to mount.</param>
    /// <param name="partition">Partition number on the disk (1-based).</param>
    /// <param name="fsType">Filesystem type to mount (for example, "ext4" or "xfs").</param>
    /// <param name="distroName">Optional WSL distribution name to target; null to mount without a specific distribution.</param>
    /// <returns>A MountResult containing details and status of the mount operation.</returns>
    Task<MountResult> ExecuteMountScriptAsync(
        int diskIndex,
        int partition,
        string fsType,
        string? distroName = null);

    /// <summary>
    /// Executes the Map-WSLShareToDrive.ps1 script to map the specified UNC network path to a drive letter.
    /// </summary>
    /// <param name="driveLetter">The drive letter to assign (e.g., 'Z').</param>
    /// <param name="targetUNC">The UNC path to map (e.g., \\server\share).</param>
    /// <returns>A MappingResult describing the outcome of the mapping operation.</returns>
    Task<MappingResult> ExecuteMappingScriptAsync(
        char driveLetter,
        string targetUNC);

    /// <summary>
    /// Executes the elevated unmount script to unmount the specified physical disk.
    /// </summary>
    /// <param name="diskIndex">Physical disk index to unmount.</param>
    /// <returns>An <see cref="UnmountResult"/> indicating the outcome of the unmount operation.</returns>
    Task<UnmountResult> ExecuteUnmountScriptAsync(int diskIndex);

    /// <summary>
    /// Unmaps the specified drive letter by executing the Unmap-DriveLetter.ps1 script.
    /// </summary>
    /// <param name="driveLetter">Drive letter to unmap (e.g., 'Z').</param>
    /// <returns>The result of the unmapping operation.</returns>
    Task<UnmappingResult> ExecuteUnmappingScriptAsync(char driveLetter);
}