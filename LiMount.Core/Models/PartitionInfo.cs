namespace LiMount.Core.Models;

/// <summary>
/// Represents information about a disk partition.
/// </summary>
public class PartitionInfo
{
    /// <summary>
    /// Partition number (1-based index).
    /// </summary>
    public int PartitionNumber { get; set; }

    /// <summary>
    /// Size of the partition in bytes.
    /// </summary>
    public long SizeBytes { get; set; }

    /// <summary>
    /// Partition label/name (if available).
    /// </summary>
    public string? Label { get; set; }

    /// <summary>
    /// Filesystem type (e.g., NTFS, FAT32, ext4, or unknown).
    /// </summary>
    public string? FileSystemType { get; set; }

    /// <summary>
    /// Whether this partition has a Windows drive letter assigned.
    /// </summary>
    public bool HasDriveLetter { get; set; }

    /// <summary>
    /// The drive letter assigned to this partition (if HasDriveLetter is true).
    /// </summary>
    public char? DriveLetter { get; set; }

    /// <summary>
    /// Heuristic indicating whether this partition is likely a Linux filesystem.
    /// Based on: no drive letter, unknown/Linux FS types, or Linux partition type GUIDs.
    /// </summary>
    public bool IsLikelyLinux { get; set; }

    /// <summary>
    /// Human-readable size (e.g., "500 GB").
    /// </summary>
    public string SizeFormatted => FormatBytes(SizeBytes);

    /// <summary>
    /// Display name for UI: "<Label or 'Partition N'> – Size"
    /// </summary>
    public string DisplayName => $"{Label ?? $"Partition {PartitionNumber}"} – {SizeFormatted}";

    /// <summary>
    /// Converts a byte count into a concise, human-readable string using binary units (base 1024).
    /// </summary>
    /// <param name="bytes">The size in bytes to format; may be zero or negative.</param>
    /// <returns>A formatted string with up to two decimal places and a unit suffix: one of "B", "KB", "MB", "GB", or "TB".</returns>
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