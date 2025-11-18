namespace LiMount.Core.Models;

/// <summary>
/// Result of executing the Mount-LinuxDiskCore.ps1 PowerShell script.
/// Parses key=value output from the script.
/// </summary>
public class MountResult
{
    /// <summary>
    /// Whether the mount operation succeeded.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// The WSL distribution name where the disk was mounted.
    /// </summary>
    public string? DistroName { get; set; }

    /// <summary>
    /// Linux path where the disk was mounted (e.g., /mnt/wsl/PHYSICALDRIVE2p1).
    /// </summary>
    public string? MountPathLinux { get; set; }

    /// <summary>
    /// UNC path to access the mounted disk from Windows (e.g., \\wsl$\Ubuntu\mnt\wsl\PHYSICALDRIVE2p1).
    /// </summary>
    public string? MountPathUNC { get; set; }

    /// <summary>
    /// Error message if the mount operation failed.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Creates a MountResult from a dictionary of key-value pairs.
    /// Expected keys: STATUS, DistroName, MountPathLinux, MountPathUNC, ErrorMessage
    /// </summary>
    /// Create a MountResult from key/value pairs produced by the mount script.
    /// <param name="values">Dictionary of output keys to values. Recognized keys: "STATUS" (sets Success when equal to "OK"), "DistroName", "MountPathLinux", "MountPathUNC", and "ErrorMessage". Missing keys result in null properties.</param>
    /// <returns>A MountResult whose Success is true when "STATUS" equals "OK" and whose other properties are populated from the corresponding dictionary entries or null if absent.</returns>
    public static MountResult FromDictionary(Dictionary<string, string> values)
    {
        var success = values.TryGetValue("STATUS", out var status) && status == "OK";

        return new MountResult
        {
            Success = success,
            DistroName = values.TryGetValue("DistroName", out var distro) ? distro : null,
            MountPathLinux = values.TryGetValue("MountPathLinux", out var linuxPath) ? linuxPath : null,
            MountPathUNC = values.TryGetValue("MountPathUNC", out var uncPath) ? uncPath : null,
            ErrorMessage = values.TryGetValue("ErrorMessage", out var error) ? error : null
        };
    }
}