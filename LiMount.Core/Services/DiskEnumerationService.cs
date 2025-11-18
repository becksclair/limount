using System.Management;
using LiMount.Core.Models;

namespace LiMount.Core.Services;

/// <summary>
/// Service for enumerating physical disks and partitions using WMI.
/// Uses Win32_DiskDrive, Win32_DiskPartition, Win32_LogicalDisk, and related classes.
///
/// Reference:
/// - https://learn.microsoft.com/en-us/windows/win32/cimwin32prov/win32-diskdrive
/// - https://learn.microsoft.com/en-us/windows/win32/wmisdk/wmi-tasks--disks-and-file-systems
/// </summary>
public class DiskEnumerationService
{
    /// <summary>
    /// Enumerates all physical disk drives on the system.
    /// </summary>
    /// <returns>List of DiskInfo with partition details</returns>
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
            // Log or handle error - for MVP, we'll just return empty list
            System.Diagnostics.Debug.WriteLine($"Error enumerating disks: {ex.Message}");
        }

        return disks;
    }

    /// <summary>
    /// Gets only disks that are candidates for mounting (non-system, non-boot).
    /// Prefers disks with at least one likely Linux partition.
    /// </summary>
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

    private List<PartitionInfo> GetPartitionsForDisk(string diskDeviceId, int diskIndex)
    {
        var partitions = new List<PartitionInfo>();

        try
        {
            // Query partitions for this disk
            // Use Win32_DiskDriveToDiskPartition association
            var partitionQuery = $"ASSOCIATORS OF {{Win32_DiskDrive.DeviceID='{diskDeviceId.Replace("\\", "\\\\")}'}} WHERE AssocClass=Win32_DiskDriveToDiskPartition";

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
            System.Diagnostics.Debug.WriteLine($"Error getting partitions for disk {diskIndex}: {ex.Message}");
        }

        return partitions;
    }

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
            System.Diagnostics.Debug.WriteLine($"Error getting logical disk for partition: {ex.Message}");
        }

        // Determine if likely Linux partition
        partitionInfo.IsLikelyLinux = DetermineIsLikelyLinux(partitionInfo, partitionType);

        return partitionInfo;
    }

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
                catch
                {
                    // Ignore errors
                }
            }
        }
    }
}
