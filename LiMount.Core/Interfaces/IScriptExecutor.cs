using LiMount.Core.Models;

namespace LiMount.Core.Interfaces;

/// <summary>
/// Interface for executing PowerShell scripts and commands.
/// Abstracts the execution details to enable testing and different execution strategies.
/// </summary>
public interface IScriptExecutor
{
    /// <summary>
    /// Executes the Mount-LinuxDiskCore.ps1 script with elevation.
    /// </summary>
    /// <param name="diskIndex">Physical disk index</param>
    /// <param name="partition">Partition number (1-based)</param>
    /// <param name="fsType">Filesystem type (ext4, xfs, etc.)</param>
    /// <param name="distroName">Optional WSL distribution name</param>
    /// <summary>
        /// Executes the elevated Mount-LinuxDiskCore.ps1 PowerShell script to mount a Linux filesystem.
        /// </summary>
        /// <param name="diskIndex">Physical disk index containing the partition to mount.</param>
        /// <param name="partition">Partition number on the disk (1-based).</param>
        /// <param name="fsType">Filesystem type to mount (e.g., "ext4", "xfs").</param>
        /// <param name="distroName">Optional WSL distribution name to target, or null to mount system-wide.</param>
        /// <returns>A <see cref="MountResult"/> describing the outcome and details of the mount operation.</returns>
    Task<MountResult> ExecuteMountScriptAsync(
        int diskIndex,
        int partition,
        string fsType,
        string? distroName = null);

    /// <summary>
    /// Executes the Map-WSLShareToDrive.ps1 script (non-elevated).
    /// </summary>
    /// <param name="driveLetter">Drive letter to map</param>
    /// <param name="targetUNC">UNC path to map</param>
    /// <summary>
        /// Maps the specified UNC network path to the given local drive letter.
        /// </summary>
        /// <param name="driveLetter">The drive letter to assign (e.g., 'Z').</param>
        /// <param name="targetUNC">The UNC path to map (e.g., \\server\share).</param>
        /// <returns>A MappingResult representing the outcome of the mapping operation.</returns>
    Task<MappingResult> ExecuteMappingScriptAsync(
        char driveLetter,
        string targetUNC);

    /// <summary>
    /// Executes the Unmount-LinuxDisk.ps1 script with elevation.
    /// </summary>
    /// <param name="diskIndex">Physical disk index to unmount</param>
    /// <summary>
/// Unmounts the specified physical disk by executing the elevated unmount script.
/// </summary>
/// <param name="diskIndex">Physical disk index to unmount.</param>
/// <returns>An <see cref="UnmountResult"/> describing the outcome of the unmount operation.</returns>
    Task<UnmountResult> ExecuteUnmountScriptAsync(int diskIndex);

    /// <summary>
    /// Executes the Unmap-DriveLetter.ps1 script (non-elevated).
    /// </summary>
    /// <param name="driveLetter">Drive letter to unmap</param>
    /// <summary>
/// Unmaps the specified drive letter by executing the Unmap-DriveLetter.ps1 script non-elevated.
/// </summary>
/// <param name="driveLetter">The drive letter to unmap.</param>
/// <returns>The result of the unmapping operation.</returns>
    Task<UnmappingResult> ExecuteUnmappingScriptAsync(char driveLetter);
}