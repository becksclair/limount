namespace LiMount.Core.Models;

/// <summary>
/// Represents information about a Windows drive letter.
/// </summary>
public class DriveLetterInfo
{
    /// <summary>
    /// The drive letter (A-Z).
    /// </summary>
    public char Letter { get; set; }

    /// <summary>
    /// Whether this drive letter is currently in use.
    /// </summary>
    public bool IsInUse { get; set; }

    /// <summary>
    /// Description of what is using this drive letter (if in use).
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Display name for UI (e.g., "Z:").
    /// </summary>
    public string DisplayName => $"{Letter}:";
}
