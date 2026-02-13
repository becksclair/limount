namespace LiMount.Core.Models;

/// <summary>
/// Represents an active mount operation that is currently in effect.
/// Used to track and reconcile mount state across application sessions.
/// </summary>
public class ActiveMount
{
    /// <summary>
    /// Unique identifier for this mount.
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Timestamp when the mount was created.
    /// </summary>
    public DateTime MountedAt { get; set; } = DateTime.Now;

    /// <summary>
    /// Physical disk index that is mounted.
    /// </summary>
    public int DiskIndex { get; set; }

    /// <summary>
    /// Partition number that is mounted.
    /// </summary>
    public int PartitionNumber { get; set; }

    /// <summary>
    /// Windows drive letter mapped to this mount when using legacy drive mapping mode.
    /// </summary>
    public char? DriveLetter { get; set; }

    /// <summary>
    /// Windows access mode used for this mount.
    /// </summary>
    public WindowsAccessMode AccessMode { get; set; } = WindowsAccessMode.NetworkLocation;

    /// <summary>
    /// Explorer Network Location name when using network location mode.
    /// </summary>
    public string? NetworkLocationName { get; set; }

    /// <summary>
    /// WSL distro name used for the mount.
    /// </summary>
    public string DistroName { get; set; } = string.Empty;

    /// <summary>
    /// Linux mount path (e.g., /mnt/wsl/PHYSICALDRIVE2p1).
    /// </summary>
    public string MountPathLinux { get; set; } = string.Empty;

    /// <summary>
    /// UNC path used for drive mapping (e.g., \\wsl$\Ubuntu\mnt\wsl\PHYSICALDRIVE2p1).
    /// </summary>
    public string MountPathUNC { get; set; } = string.Empty;

    /// <summary>
    /// Whether this mount has been verified to still exist.
    /// Used during reconciliation on startup.
    /// </summary>
    public bool IsVerified { get; set; }

    /// <summary>
    /// Last time this mount was verified/accessed.
    /// </summary>
    public DateTime? LastVerified { get; set; }
}
