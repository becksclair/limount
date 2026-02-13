namespace LiMount.Core.Models;

/// <summary>
/// Represents the type of mount operation for history tracking.
/// </summary>
public enum MountHistoryOperationType
{
    /// <summary>
    /// Mount and map operation.
    /// </summary>
    Mount,
    
    /// <summary>
    /// Unmount and unmap operation.
    /// </summary>
    Unmount
}

/// <summary>
/// Represents a historical record of a mount or unmount operation.
/// </summary>
public class MountHistoryEntry
{
    /// <summary>
    /// Unique identifier for this history entry.
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Timestamp when the operation occurred.
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.Now;

    /// <summary>
    /// Type of operation.
    /// </summary>
    public MountHistoryOperationType OperationType { get; set; }

    /// <summary>
    /// Index of the disk involved in the operation.
    /// </summary>
    public int DiskIndex { get; set; }

    /// <summary>
    /// Partition number involved in the mount operation (null for unmount).
    /// </summary>
    public int? PartitionNumber { get; set; }

    /// <summary>
    /// Drive letter that was mapped/unmapped in legacy mode (if any).
    /// </summary>
    public char? DriveLetter { get; set; }

    /// <summary>
    /// Windows access mode used for this operation.
    /// </summary>
    public WindowsAccessMode AccessMode { get; set; }

    /// <summary>
    /// Explorer Network Location name used/removed in network location mode.
    /// </summary>
    public string? NetworkLocationName { get; set; }

    /// <summary>
    /// WSL distribution name used for the mount operation (if any).
    /// </summary>
    public string? DistroName { get; set; }

    /// <summary>
    /// Linux mount path (for mount operations).
    /// </summary>
    public string? MountPathLinux { get; set; }

    /// <summary>
    /// UNC path used for drive mapping (for mount operations).
    /// </summary>
    public string? MountPathUNC { get; set; }

    /// <summary>
    /// Whether the operation succeeded.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Error message if the operation failed.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// The step that failed (if any): "mount", "map", "unmap", "unmount", or null if successful.
    /// </summary>
    public string? FailedStep { get; set; }

    /// <summary>
    /// Creates a mount history entry from a MountAndMapResult.
    /// </summary>
    public static MountHistoryEntry FromMountResult(MountAndMapResult result)
    {
        return new MountHistoryEntry
        {
            Timestamp = result.Timestamp,
            OperationType = MountHistoryOperationType.Mount,
            DiskIndex = result.DiskIndex,
            PartitionNumber = result.Partition,
            DriveLetter = result.DriveLetter,
            AccessMode = result.AccessMode,
            NetworkLocationName = result.NetworkLocationName,
            DistroName = result.DistroName,
            MountPathLinux = result.MountPathLinux,
            MountPathUNC = result.MountPathUNC,
            Success = result.Success,
            ErrorMessage = result.ErrorMessage,
            FailedStep = result.FailedStep
        };
    }

    /// <summary>
    /// Creates an unmount history entry from an UnmountAndUnmapResult.
    /// </summary>
    public static MountHistoryEntry FromUnmountResult(UnmountAndUnmapResult result)
    {
        return new MountHistoryEntry
        {
            Timestamp = result.Timestamp,
            OperationType = MountHistoryOperationType.Unmount,
            DiskIndex = result.DiskIndex,
            DriveLetter = result.DriveLetter,
            AccessMode = result.AccessMode,
            NetworkLocationName = result.NetworkLocationName,
            Success = result.Success,
            ErrorMessage = result.ErrorMessage,
            FailedStep = result.FailedStep
        };
    }
}
