namespace LiMount.Core.Models;

/// <summary>
/// Represents created Windows-side access details for an active mount.
/// </summary>
public sealed class WindowsAccessInfo
{
    /// <summary>
    /// Access mode used for this mount.
    /// </summary>
    public WindowsAccessMode AccessMode { get; set; }

    /// <summary>
    /// UNC path to the mounted filesystem.
    /// </summary>
    public string AccessPathUNC { get; set; } = string.Empty;

    /// <summary>
    /// Drive letter when access mode is legacy drive mapping.
    /// </summary>
    public char? DriveLetter { get; set; }

    /// <summary>
    /// Explorer Network Location name when access mode is network location.
    /// </summary>
    public string? NetworkLocationName { get; set; }
}

