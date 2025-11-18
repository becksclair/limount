namespace LiMount.Core.Models;

/// <summary>
/// Combined result of the unmount and unmap workflow.
/// </summary>
public class UnmountAndUnmapResult
{
    /// <summary>
    /// Whether the entire workflow succeeded.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// The disk index that was unmounted.
    /// </summary>
    public int DiskIndex { get; set; }

    /// <summary>
    /// The drive letter that was unmapped (if any).
    /// </summary>
    public char? DriveLetter { get; set; }

    /// <summary>
    /// Error message if the workflow failed.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Which step failed (if any): "unmap", "unmount", or null if successful.
    /// </summary>
    public string? FailedStep { get; set; }

    /// <summary>
    /// Timestamp when the operation completed.
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.Now;

    /// <summary>
    /// Creates a successful result for the specified disk and optional drive letter.
    /// </summary>
    /// <param name="diskIndex">Index of the disk that was successfully unmounted and unmapped.</param>
    /// <param name="driveLetter">Optional drive letter that was unmapped; null if none.</param>
    /// <returns>An UnmountAndUnmapResult marked as successful with the provided disk index and optional drive letter.</returns>
    public static UnmountAndUnmapResult CreateSuccess(
        int diskIndex,
        char? driveLetter = null)
    {
        return new UnmountAndUnmapResult
        {
            Success = true,
            DiskIndex = diskIndex,
            DriveLetter = driveLetter
        };
    }

    /// <summary>
    /// Creates a result representing a failed unmount-and-unmap operation for the specified disk.
    /// </summary>
    /// <param name="diskIndex">The index of the disk on which the operation failed.</param>
    /// <param name="errorMessage">A human-readable error message describing the failure.</param>
    /// <param name="failedStep">The step that failed (for example "unmap" or "unmount").</param>
    /// <returns>An <see cref="UnmountAndUnmapResult"/> instance marked as failed with the provided details.</returns>
    public static UnmountAndUnmapResult CreateFailure(
        int diskIndex,
        string errorMessage,
        string failedStep)
    {
        return new UnmountAndUnmapResult
        {
            Success = false,
            DiskIndex = diskIndex,
            ErrorMessage = errorMessage,
            FailedStep = failedStep
        };
    }
}