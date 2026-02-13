namespace LiMount.Core.Models;

/// <summary>
/// Request payload for creating Windows-side access to a mounted UNC path.
/// </summary>
public sealed class WindowsAccessRequest
{
    /// <summary>
    /// Selected Windows access mode.
    /// </summary>
    public WindowsAccessMode AccessMode { get; set; }

    /// <summary>
    /// Target UNC path that should be exposed to Windows.
    /// </summary>
    public string TargetUNC { get; set; } = string.Empty;

    /// <summary>
    /// Drive letter to use when <see cref="AccessMode"/> is <see cref="WindowsAccessMode.DriveLetterLegacy"/>.
    /// </summary>
    public char? DriveLetter { get; set; }

    /// <summary>
    /// Optional custom network location name for <see cref="WindowsAccessMode.NetworkLocation"/>.
    /// </summary>
    public string? NetworkLocationName { get; set; }

    /// <summary>
    /// Mounted disk index, used for diagnostics/default naming.
    /// </summary>
    public int DiskIndex { get; set; }

    /// <summary>
    /// Mounted partition number, used for diagnostics/default naming.
    /// </summary>
    public int PartitionNumber { get; set; }
}

