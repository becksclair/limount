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
    /// Creates a successful result.
    /// <summary>
    /// Create a successful result for an unmount-and-unmap operation for the specified disk.
    /// </summary>
    /// <param name="diskIndex">The index of the disk that was unmounted.</param>
    /// <param name="driveLetter">The unmapped drive letter, or <c>null</c> if none.</param>
    /// <returns>An <see cref="UnmountAndUnmapResult"/> with <see cref="UnmountAndUnmapResult.Success"/> set to <c>true</c>, <see cref="UnmountAndUnmapResult.DiskIndex"/> set to <paramref name="diskIndex"/>, and <see cref="UnmountAndUnmapResult.DriveLetter"/> set to <paramref name="driveLetter"/>.</returns>
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
    /// Creates a failure result.
    /// <summary>
    /// Creates an UnmountAndUnmapResult representing a failed unmount-and-unmap workflow.
    /// </summary>
    /// <param name="diskIndex">Index of the disk on which the workflow failed.</param>
    /// <param name="errorMessage">Error message describing the failure.</param>
    /// <param name="failedStep">The workflow step that failed (e.g., "unmap" or "unmount").</param>
    /// <returns>An UnmountAndUnmapResult with <c>Success</c> set to <c>false</c>, and <c>DiskIndex</c>, <c>ErrorMessage</c>, and <c>FailedStep</c> populated.</returns>
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