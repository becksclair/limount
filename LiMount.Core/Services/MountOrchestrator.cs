using System.Runtime.Versioning;
using LiMount.Core.Interfaces;
using LiMount.Core.Models;

namespace LiMount.Core.Services;

/// <summary>
/// Orchestrates the complete mount workflow: WSL mounting + drive letter mapping.
/// Implements retry logic and detailed progress reporting.
/// </summary>
[SupportedOSPlatform("windows")]
public class MountOrchestrator : IMountOrchestrator
{
    private readonly IScriptExecutor _scriptExecutor;

    /// <summary>
    /// Creates a MountOrchestrator that uses the provided script executor to perform mounting and drive-mapping operations.
    /// </summary>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="scriptExecutor"/> is null.</exception>
    public MountOrchestrator(IScriptExecutor scriptExecutor)
    {
        _scriptExecutor = scriptExecutor ?? throw new ArgumentNullException(nameof(scriptExecutor));
    }

    /// <summary>
    /// Orchestrates mounting a disk inside WSL and mapping the resulting share to a Windows drive letter.
    /// </summary>
    /// <param name="diskIndex">Index of the physical disk to mount.</param>
    /// <param name="partition">Partition number on the disk to mount.</param>
    /// <param name="driveLetter">Drive letter to assign to the mapped share.</param>
    /// <param name="fsType">Filesystem type to use for the mount (default: "ext4").</param>
    /// <param name="distroName">Optional WSL distribution name to perform the mount within.</param>
    /// <param name="progress">Optional progress reporter that receives human-readable status and warning messages.</param>
    /// <returns>
    /// A <see cref="MountAndMapResult"/> describing the outcome:
    /// - On success: includes diskIndex, partition, assigned driveLetter, distro name, Linux mount path, and UNC path.
    /// - On failure: includes diskIndex, partition, an error message, and the step that failed ("mount" or "map").
    /// </returns>
    public async Task<MountAndMapResult> MountAndMapAsync(
        int diskIndex,
        int partition,
        char driveLetter,
        string fsType = "ext4",
        string? distroName = null,
        IProgress<string>? progress = null)
    {
        progress?.Report($"Starting mount operation for disk {diskIndex} partition {partition}...");

        // Step 1: Mount disk in WSL
        progress?.Report("Mounting disk in WSL (this may take a moment)...");

        var mountResult = await _scriptExecutor.ExecuteMountScriptAsync(
            diskIndex,
            partition,
            fsType,
            distroName);

        if (!mountResult.Success)
        {
            progress?.Report($"Mount failed: {mountResult.ErrorMessage}");
            return MountAndMapResult.CreateFailure(
                diskIndex,
                partition,
                mountResult.ErrorMessage ?? "Unknown error during mount",
                "mount");
        }

        progress?.Report($"Disk mounted successfully at {mountResult.MountPathLinux}");

        // Verify UNC path is accessible (with retry)
        var uncPath = mountResult.MountPathUNC;
        if (!string.IsNullOrEmpty(uncPath))
        {
            progress?.Report("Verifying WSL share accessibility...");

            bool uncAccessible = false;
            for (int i = 0; i < 5; i++)
            {
                if (Directory.Exists(uncPath))
                {
                    uncAccessible = true;
                    break;
                }
                await Task.Delay(500);
            }

            if (!uncAccessible)
            {
                progress?.Report($"Warning: UNC path {uncPath} not immediately accessible");
            }
        }

        // Step 2: Map drive letter
        progress?.Report($"Mapping drive letter {driveLetter}:...");

        var mappingResult = await _scriptExecutor.ExecuteMappingScriptAsync(
            driveLetter,
            uncPath ?? string.Empty);

        if (!mappingResult.Success)
        {
            progress?.Report($"Drive mapping failed: {mappingResult.ErrorMessage}");
            return MountAndMapResult.CreateFailure(
                diskIndex,
                partition,
                mappingResult.ErrorMessage ?? "Unknown error during mapping",
                "map");
        }

        progress?.Report($"Successfully mapped as {driveLetter}:");

        // Return success
        return MountAndMapResult.CreateSuccess(
            diskIndex,
            partition,
            driveLetter,
            mountResult.DistroName ?? "Unknown",
            mountResult.MountPathLinux ?? string.Empty,
            mountResult.MountPathUNC ?? string.Empty);
    }
}