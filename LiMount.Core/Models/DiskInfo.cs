namespace LiMount.Core.Models;

/// <summary>
/// Represents information about a physical disk drive.
/// </summary>
public class DiskInfo
{
    /// <summary>
    /// Disk index (0-based, matches Win32_DiskDrive Index).
    /// </summary>
    public int Index { get; set; }

    /// <summary>
    /// Physical device ID (e.g., "\\.\PHYSICALDRIVE2").
    /// </summary>
    public string DeviceId { get; set; } = string.Empty;

    /// <summary>
    /// Disk model/manufacturer name.
    /// </summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>
    /// Total size of the disk in bytes.
    /// </summary>
    public long SizeBytes { get; set; }

    /// <summary>
    /// Whether this disk contains the Windows system partition.
    /// </summary>
    public bool IsSystem { get; set; }

    /// <summary>
    /// Whether this disk contains the boot partition.
    /// </summary>
    public bool IsBoot { get; set; }

    /// <summary>
    /// List of partitions on this disk.
    /// </summary>
    public IReadOnlyList<PartitionInfo> Partitions { get; set; } = Array.Empty<PartitionInfo>();

    /// <summary>
    /// Human-readable size (e.g., "500 GB").
    /// </summary>
    public string SizeFormatted => FormatBytes(SizeBytes);

    /// <summary>
    /// Display name for UI: "Index – Model – Size"
    /// </summary>
    public string DisplayName => $"{Index} – {Model} – {SizeFormatted}";

    /// <summary>
    /// Whether this disk has at least one likely Linux partition.
    /// </summary>
    public bool HasLinuxPartitions => Partitions.Any(p => p.IsLikelyLinux);

    /// <summary>
    /// Format a byte count as a human-readable string with an appropriate unit suffix.
    /// </summary>
    /// <param name="bytes">The number of bytes to format.</param>
    /// <returns>The value expressed in B, KB, MB, GB, or TB with up to two decimal places (for example, "1.5 GB").</returns>
    private static string FormatBytes(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len = len / 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }
}