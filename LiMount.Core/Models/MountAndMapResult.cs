namespace LiMount.Core.Models;

/// <summary>
/// Combined result of the mount and map workflow.
/// </summary>
public class MountAndMapResult
{
    /// <summary>
    /// Whether the entire workflow succeeded.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// The disk index that was mounted.
    /// </summary>
    public int DiskIndex { get; set; }

    /// <summary>
    /// The partition number that was mounted.
    /// </summary>
    public int Partition { get; set; }

    /// <summary>
    /// The drive letter that was mapped.
    /// </summary>
    public char? DriveLetter { get; set; }

    /// <summary>
    /// The WSL distribution name where the disk was mounted.
    /// </summary>
    public string? DistroName { get; set; }

    /// <summary>
    /// Linux mount path (e.g., /mnt/wsl/PHYSICALDRIVE2p1).
    /// </summary>
    public string? MountPathLinux { get; set; }

    /// <summary>
    /// Windows UNC path (e.g., \\wsl$\Ubuntu\mnt\wsl\PHYSICALDRIVE2p1).
    /// </summary>
    public string? MountPathUNC { get; set; }

    /// <summary>
    /// Error message if the workflow failed.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Which step failed (if any): "mount", "map", or null if successful.
    /// </summary>
    public string? FailedStep { get; set; }

    /// <summary>
    /// Timestamp when the operation completed.
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.Now;

    /// <summary>
    /// Creates a successful result.
    /// <summary>
    /// Create a MountAndMapResult representing a successful mount-and-map operation.
    /// </summary>
    /// <param name="diskIndex">The index of the disk that was mounted.</param>
    /// <param name="partition">The partition number that was mounted.</param>
    /// <param name="driveLetter">The Windows drive letter that was mapped.</param>
    /// <param name="distroName">The WSL distribution name where the disk was mounted.</param>
    /// <param name="mountPathLinux">The Linux mount path (e.g., /mnt/wsl/disks/disk{diskIndex}/part{partition}).</param>
    /// <param name="mountPathUNC">The Windows UNC path to the mounted volume (e.g., \\wsl$\{distroName}\mnt\...).</param>
    /// <returns>A MountAndMapResult with Success set to true and the provided disk, partition, drive, distro, and mount path values.</returns>
    public static MountAndMapResult CreateSuccess(
        int diskIndex,
        int partition,
        char driveLetter,
        string distroName,
        string mountPathLinux,
        string mountPathUNC)
    {
        return new MountAndMapResult
        {
            Success = true,
            DiskIndex = diskIndex,
            Partition = partition,
            DriveLetter = driveLetter,
            DistroName = distroName,
            MountPathLinux = mountPathLinux,
            MountPathUNC = mountPathUNC
        };
    }

    /// <summary>
    /// Creates a failure result.
    /// <summary>
    /// Creates a MountAndMapResult representing a failed mount-and-map operation.
    /// </summary>
    /// <param name="errorMessage">A descriptive message explaining why the operation failed.</param>
    /// <param name="failedStep">The step that failed (e.g., "mount" or "map").</param>
    /// <returns>A MountAndMapResult with Success set to false, DiskIndex and Partition set from the arguments, ErrorMessage and FailedStep populated, and other fields left at their defaults.</returns>
    public static MountAndMapResult CreateFailure(
        int diskIndex,
        int partition,
        string errorMessage,
        string failedStep)
    {
        return new MountAndMapResult
        {
            Success = false,
            DiskIndex = diskIndex,
            Partition = partition,
            ErrorMessage = errorMessage,
            FailedStep = failedStep
        };
    }
}