using System.Runtime.Versioning;
using Microsoft.Extensions.Options;
using LiMount.Core.Interfaces;
using LiMount.Core.Models;
using LiMount.Core.Configuration;

namespace LiMount.Core.Services;

/// <summary>
/// Orchestrates the complete mount workflow: WSL mounting + drive letter mapping.
/// Implements retry logic and detailed progress reporting.
/// </summary>
[SupportedOSPlatform("windows")]
public class MountOrchestrator : IMountOrchestrator
{
    private readonly IScriptExecutor _scriptExecutor;
    private readonly IMountHistoryService? _historyService;
    private readonly MountOperationsConfig _config;
    private readonly int _validatedUncRetries;
    private readonly int _validatedUncDelayMs;

    /// <summary>
    /// Initializes a new instance of <see cref="MountOrchestrator"/> using the provided script executor.
    /// </summary>
    /// <param name="scriptExecutor">The script executor for running PowerShell scripts.</param>
    /// <param name="config">Configuration for mount operations.</param>
    /// <param name="historyService">Optional history service for tracking mount operations.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="scriptExecutor"/> or <paramref name="config"/> is null.</exception>
    public MountOrchestrator(
        IScriptExecutor scriptExecutor,
        IOptions<LiMountConfiguration> config,
        IMountHistoryService? historyService = null)
    {
        ArgumentNullException.ThrowIfNull(scriptExecutor);
        ArgumentNullException.ThrowIfNull(config);
        _scriptExecutor = scriptExecutor;
        _config = config.Value.MountOperations;
        _historyService = historyService;

        // Validate and clamp UNC retry configuration to prevent unsafe values
        _validatedUncRetries = Math.Max(0, Math.Min(100, _config.UncAccessibilityRetries));
        _validatedUncDelayMs = Math.Max(10, Math.Min(5000, _config.UncAccessibilityDelayMs));
    }

    /// <summary>
    /// Orchestrates mounting a disk inside WSL and mapping a Windows drive letter to the resulting UNC share.
    /// </summary>
    /// <param name="diskIndex">Zero-based index of the disk to mount. Must be non-negative.</param>
    /// <param name="partition">Partition number on the disk to mount. Must be greater than 0.</param>
    /// <param name="driveLetter">Drive letter to assign for the Windows mapping. Must be a valid letter (A-Z).</param>
    /// <param name="fsType">Filesystem type to use for mounting (e.g., "ext4"). Cannot be empty or null.</param>
    /// <param name="distroName">Optional WSL distribution name to perform the mount under; if null, a default or detection may be used.</param>
    /// <param name="progress">Optional progress reporter that receives status messages during the operation.</param>
    /// <returns>
    /// A MountAndMapResult describing the outcome. On success the result contains the disk and partition identifiers, the mapped drive letter, the distribution name, the Linux mount path, and the UNC path. On failure the result contains an error message and the stage that failed ("validation", "mount", or "map").
    /// </returns>
    public async Task<MountAndMapResult> MountAndMapAsync(
        int diskIndex,
        int partition,
        char driveLetter,
        string fsType = "ext4",
        string? distroName = null,
        IProgress<string>? progress = null)
    {
        // Validate parameters
        if (diskIndex < 0)
        {
            return MountAndMapResult.CreateFailure(diskIndex, partition, "Disk index must be non-negative", "validation");
        }

        if (partition < 1)
        {
            return MountAndMapResult.CreateFailure(diskIndex, partition, "Partition number must be greater than 0", "validation");
        }

        if (!char.IsLetter(driveLetter))
        {
            return MountAndMapResult.CreateFailure(diskIndex, partition, "Drive letter must be a valid letter (A-Z)", "validation");
        }

        if (string.IsNullOrWhiteSpace(fsType))
        {
            return MountAndMapResult.CreateFailure(diskIndex, partition, "Filesystem type cannot be empty", "validation");
        }

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
            var failureResult = MountAndMapResult.CreateFailure(
                diskIndex,
                partition,
                mountResult.ErrorMessage ?? "Unknown error during mount",
                "mount");

            // Log failure to history
            if (_historyService != null)
            {
                var historyEntry = MountHistoryEntry.FromMountResult(failureResult);
                await _historyService.AddEntryAsync(historyEntry);
            }

            return failureResult;
        }

        progress?.Report($"Disk mounted successfully at {mountResult.MountPathLinux}");

        // Verify UNC path is accessible (with retry)
        var uncPath = mountResult.MountPathUNC;
        if (!string.IsNullOrEmpty(uncPath))
        {
            progress?.Report("Verifying WSL share accessibility...");

            bool uncAccessible = false;
            for (int i = 0; i < _validatedUncRetries; i++)
            {
                if (Directory.Exists(uncPath))
                {
                    uncAccessible = true;
                    break;
                }
                await Task.Delay(_validatedUncDelayMs);
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
            var failureResult = MountAndMapResult.CreateFailure(
                diskIndex,
                partition,
                mappingResult.ErrorMessage ?? "Unknown error during mapping",
                "map");

            // Log failure to history
            if (_historyService != null)
            {
                var historyEntry = MountHistoryEntry.FromMountResult(failureResult);
                await _historyService.AddEntryAsync(historyEntry);
            }

            return failureResult;
        }

        progress?.Report($"Successfully mapped as {driveLetter}:");

        // Create success result
        var result = MountAndMapResult.CreateSuccess(
            diskIndex,
            partition,
            driveLetter,
            mountResult.DistroName ?? "Unknown",
            mountResult.MountPathLinux ?? string.Empty,
            mountResult.MountPathUNC ?? string.Empty);

        // Log to history
        if (_historyService != null)
        {
            var historyEntry = MountHistoryEntry.FromMountResult(result);
            await _historyService.AddEntryAsync(historyEntry);
        }

        return result;
    }
}