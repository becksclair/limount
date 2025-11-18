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
    /// <returns>Result of the mount operation</returns>
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
    /// <returns>Result of the mapping operation</returns>
    Task<MappingResult> ExecuteMappingScriptAsync(
        char driveLetter,
        string targetUNC);

    /// <summary>
    /// Executes the Unmount-LinuxDisk.ps1 script with elevation.
    /// </summary>
    /// <param name="diskIndex">Physical disk index to unmount</param>
    /// <returns>Result of the unmount operation</returns>
    Task<UnmountResult> ExecuteUnmountScriptAsync(int diskIndex);

    /// <summary>
    /// Executes the Unmap-DriveLetter.ps1 script (non-elevated).
    /// </summary>
    /// <param name="driveLetter">Drive letter to unmap</param>
    /// <returns>Result of the unmapping operation</returns>
    Task<UnmappingResult> ExecuteUnmappingScriptAsync(char driveLetter);
}
