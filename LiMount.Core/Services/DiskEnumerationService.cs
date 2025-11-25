using System.Management;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using LiMount.Core.Interfaces;
using LiMount.Core.Models;

namespace LiMount.Core.Services;

/// <summary>
/// Service for enumerating physical disks and partitions using WMI.
/// Uses Win32_DiskDrive, Win32_DiskPartition, Win32_LogicalDisk, and related classes.
///
/// This service is Windows-only as it relies on WMI (Windows Management Instrumentation).
///
/// Reference:
/// - https://learn.microsoft.com/en-us/windows/win32/cimwin32prov/win32-diskdrive
/// - https://learn.microsoft.com/en-us/windows/win32/wmisdk/wmi-tasks--disks-and-file-systems
/// </summary>
[SupportedOSPlatform("windows")]
public class DiskEnumerationService : IDiskEnumerationService
{
    private readonly ILogger<DiskEnumerationService> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="DiskEnumerationService"/>.
    /// </summary>
    /// <param name="logger">Logger for diagnostic information.</param>
    public DiskEnumerationService(ILogger<DiskEnumerationService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Enumerates physical disk drives on the system and returns DiskInfo objects populated with their partition details.
    /// </summary>
    /// <returns>A list of DiskInfo objects for each detected physical disk, each including associated PartitionInfo entries; the list may be empty if enumeration encounters an error.</returns>
    public IReadOnlyList<DiskInfo> GetDisks()
    {
        var disks = new List<DiskInfo>();

        try
        {
            using var diskSearcher = new ManagementObjectSearcher("SELECT * FROM Win32_DiskDrive");
            var diskCollection = diskSearcher.Get();

            foreach (ManagementObject disk in diskCollection)
            {
                var diskInfo = ParseDiskInfo(disk);
                disks.Add(diskInfo);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error enumerating disks via WMI. Returning empty disk list.");
        }

        return disks;
    }

    /// <summary>
    /// Get candidate disks excluding system and boot disks, preferring disks that contain Linux-like partitions.
    /// </summary>
    /// <returns>A read-only list of DiskInfo objects that are not marked as system or boot, ordered by HasLinuxPartitions (descending) then by Index.</returns>
    public IReadOnlyList<DiskInfo> GetCandidateDisks()
    {
        var allDisks = GetDisks();

        // Filter out system and boot disks
        var candidateDisks = allDisks
            .Where(d => !d.IsSystem && !d.IsBoot)
            .OrderByDescending(d => d.HasLinuxPartitions) // Prefer disks with Linux partitions
            .ThenBy(d => d.Index)
            .ToList();

        return candidateDisks;
    }

    /// <summary>
    /// Creates a DiskInfo from a Win32_DiskDrive WMI object, including its partitions and system/boot flags.
    /// </summary>
    /// <param name="disk">WMI ManagementObject representing a Win32_DiskDrive.</param>
    /// <returns>A DiskInfo populated with index, device ID, model, size, partitions, and system/boot indicators.</returns>
    private DiskInfo ParseDiskInfo(ManagementObject disk)
    {
        var index = Convert.ToInt32(disk["Index"]);
        var deviceId = disk["DeviceID"]?.ToString() ?? $"\\\\.\\PHYSICALDRIVE{index}";
        var model = disk["Model"]?.ToString() ?? "Unknown";
        var sizeBytes = disk["Size"] != null ? Convert.ToInt64(disk["Size"]) : 0;

        var diskInfo = new DiskInfo
        {
            Index = index,
            DeviceId = deviceId,
            Model = model,
            SizeBytes = sizeBytes,
            IsSystem = false, // Will be determined below
            IsBoot = false,   // Will be determined below
            Partitions = GetPartitionsForDisk(deviceId, index)
        };

        // Determine if this is system or boot disk
        DetermineSystemAndBootFlags(diskInfo);

        return diskInfo;
    }

    /// <summary>
    /// Enumerates partitions associated with the specified physical disk and returns their parsed PartitionInfo objects.
    /// </summary>
    /// <param name="diskDeviceId">The WMI DeviceID of the disk (as returned by Win32_DiskDrive.DeviceID).</param>
    /// <param name="diskIndex">The zero-based disk index used to identify the disk in logs and diagnostics.</param>
    /// <returns>A list of PartitionInfo objects for the disk; returns an empty list if no partitions are found or an error occurs.</returns>
    private List<PartitionInfo> GetPartitionsForDisk(string diskDeviceId, int diskIndex)
    {
        var partitions = new List<PartitionInfo>();

        try
        {
            // Query partitions for this disk directly using DiskIndex
            // This is more reliable than ASSOCIATORS query which has escaping issues with DeviceID
            var partitionQuery = $"SELECT * FROM Win32_DiskPartition WHERE DiskIndex = {diskIndex}";

            using var partitionSearcher = new ManagementObjectSearcher(partitionQuery);
            var partitionCollection = partitionSearcher.Get();

            foreach (ManagementObject partition in partitionCollection)
            {
                var partitionInfo = ParsePartitionInfo(partition);
                partitions.Add(partitionInfo);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error getting partitions for disk {DiskIndex}", diskIndex);
        }

        return partitions;
    }

    /// <summary>
    /// Creates a PartitionInfo from a Win32_DiskPartition WMI object, including size, partition number, volume details when available, and a heuristic Linux-likelihood flag.
    /// </summary>
    /// <param name="partition">The WMI ManagementObject representing a Win32_DiskPartition.</param>
    /// <returns>A populated PartitionInfo with PartitionNumber, SizeBytes, optional Label, FileSystemType, drive letter information, and IsLikelyLinux.</returns>
    private PartitionInfo ParsePartitionInfo(ManagementObject partition)
    {
        // Get basic partition info
        var partitionNumber = partition["Index"] != null ? Convert.ToInt32(partition["Index"]) + 1 : 0; // Index is 0-based
        var sizeBytes = partition["Size"] != null ? Convert.ToInt64(partition["Size"]) : 0;
        var partitionType = partition["Type"]?.ToString() ?? "";

        var partitionInfo = new PartitionInfo
        {
            PartitionNumber = partitionNumber,
            SizeBytes = sizeBytes,
            Label = null,
            FileSystemType = null,
            HasDriveLetter = false,
            DriveLetter = null,
            IsLikelyLinux = false
        };

        // Try to get logical disk (volume) info
        try
        {
            var deviceId = partition["DeviceID"]?.ToString();
            if (!string.IsNullOrEmpty(deviceId))
            {
                var logicalDiskQuery = $"ASSOCIATORS OF {{Win32_DiskPartition.DeviceID='{deviceId.Replace("\\", "\\\\")}'}} WHERE AssocClass=Win32_LogicalDiskToPartition";

                using var logicalDiskSearcher = new ManagementObjectSearcher(logicalDiskQuery);
                var logicalDiskCollection = logicalDiskSearcher.Get();

                foreach (ManagementObject logicalDisk in logicalDiskCollection)
                {
                    partitionInfo.Label = logicalDisk["VolumeName"]?.ToString();
                    partitionInfo.FileSystemType = logicalDisk["FileSystem"]?.ToString();

                    var driveLetter = logicalDisk["DeviceID"]?.ToString();
                    if (!string.IsNullOrEmpty(driveLetter) && driveLetter.Length > 0 && char.IsLetter(driveLetter[0]))
                    {
                        partitionInfo.HasDriveLetter = true;
                        partitionInfo.DriveLetter = char.ToUpperInvariant(driveLetter[0]);
                    }

                    break; // Usually only one logical disk per partition
                }
            }
        }
        catch (Exception ex)
        {
                _logger.LogWarning(ex, "Error getting logical disk for partition");
        }

        // Determine if likely Linux partition
        partitionInfo.IsLikelyLinux = DetermineIsLikelyLinux(partitionInfo, partitionType);

        return partitionInfo;
    }

    /// <summary>
    /// Determines whether a partition is likely a Linux partition based on drive letter presence, filesystem type, and partition type hints.
    /// </summary>
    /// <param name="partition">Partition metadata used to inspect drive-letter assignment and filesystem type.</param>
    /// <param name="partitionType">The partition type string (from WMI) used to identify Linux or swap partition types.</param>
    /// <returns>`true` if the partition is likely Linux, `false` otherwise.</returns>
    private bool DetermineIsLikelyLinux(PartitionInfo partition, string partitionType)
    {
        // Heuristics for Linux partitions:
        // 1. No drive letter assigned (most common indicator)
        // 2. Unknown or Linux-specific filesystem types
        // 3. Partition type indicates Linux (e.g., contains "Linux" in the type string)

        if (partition.HasDriveLetter)
        {
            // If it has a Windows drive letter, it's likely not a Linux-only partition
            return false;
        }

        // Check filesystem type
        if (!string.IsNullOrEmpty(partition.FileSystemType))
        {
            var fsType = partition.FileSystemType.ToLowerInvariant();
            // Known Linux filesystems
            if (fsType.Contains("ext") || fsType.Contains("xfs") || fsType.Contains("btrfs") ||
                fsType.Contains("f2fs") || fsType.Contains("jfs") || fsType.Contains("reiser"))
            {
                return true;
            }

            // Known Windows filesystems
            if (fsType.Contains("ntfs") || fsType.Contains("fat") || fsType.Contains("exfat") || fsType.Contains("refs"))
            {
                return false;
            }
        }

        // Check partition type string
        if (!string.IsNullOrEmpty(partitionType))
        {
            var type = partitionType.ToLowerInvariant();
            if (type.Contains("linux") || type.Contains("swap"))
            {
                return true;
            }
        }

        // If partition has no drive letter and unknown FS type, it's possibly Linux
        // This is a best-guess heuristic
        if (!partition.HasDriveLetter && string.IsNullOrEmpty(partition.FileSystemType))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Marks the provided disk as the system and boot disk if any of its partitions correspond to the Windows system drive.
    /// </summary>
    /// <param name="diskInfo">The disk to inspect; its <see cref="DiskInfo.IsSystem"/> and <see cref="DiskInfo.IsBoot"/> flags will be set to true when a matching partition is found.</param>
    private void DetermineSystemAndBootFlags(DiskInfo diskInfo)
    {
        // Check if any partition on this disk has the system drive (usually C:)
        foreach (var partition in diskInfo.Partitions)
        {
            if (partition.DriveLetter.HasValue)
            {
                var letter = partition.DriveLetter.Value;

                // C: is typically the system drive
                if (letter == 'C')
                {
                    diskInfo.IsSystem = true;
                    diskInfo.IsBoot = true;
                    return;
                }

                // Check if this drive is the Windows directory
                try
                {
                    var windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                    if (!string.IsNullOrEmpty(windowsDir) && windowsDir.Length > 0)
                    {
                        var systemDriveLetter = char.ToUpperInvariant(windowsDir[0]);
                        if (letter == systemDriveLetter)
                        {
                            diskInfo.IsSystem = true;
                            diskInfo.IsBoot = true;
                            return;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to get Windows folder while determining system/boot disk");
                }
            }
        }
    }
}