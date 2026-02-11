using LiMount.Core.Services;

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
    /// Whether the disk/partition was already mounted before this operation.
    /// </summary>
    public bool AlreadyMounted { get; set; }

    /// <summary>
    /// Whether UNC path verification succeeded when performed by the script.
    /// Null when the script did not report verification.
    /// </summary>
    public bool? UncVerified { get; set; }

    /// <summary>
    /// Error message if the mount operation failed.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Machine-readable mount error code (for example, XFS_UNSUPPORTED_FEATURES).
    /// </summary>
    public string? ErrorCode { get; set; }

    /// <summary>
    /// User-actionable guidance associated with a mount failure.
    /// </summary>
    public string? ErrorHint { get; set; }

    /// <summary>
    /// Short sanitized diagnostic excerpt captured from dmesg.
    /// </summary>
    public string? DmesgSummary { get; set; }

    /// <summary>
    /// Creates a MountResult from a dictionary of key-value pairs.
    /// Expected keys: STATUS, DistroName, MountPathLinux, MountPathUNC, AlreadyMounted, UncVerified,
    /// ErrorMessage, ErrorCode, ErrorHint, DmesgSummary.
    /// </summary>
    /// <param name="values">Dictionary of output keys to values. Recognized keys: "STATUS" (sets Success when equal to "OK"), "DistroName", "MountPathLinux", "MountPathUNC", "AlreadyMounted", "UncVerified", "ErrorMessage", "ErrorCode", "ErrorHint", and "DmesgSummary". Missing keys result in null/default properties.</param>
    /// <returns>A MountResult whose Success is true when "STATUS" equals "OK" and whose other properties are populated from the corresponding dictionary entries or null if absent.</returns>
    public static MountResult FromDictionary(Dictionary<string, string> values)
    {
        var success = KeyValueOutputParser.IsSuccess(values);

        return new MountResult
        {
            Success = success,
            DistroName = values.TryGetValue("DistroName", out var distro) ? distro : null,
            MountPathLinux = values.TryGetValue("MountPathLinux", out var linuxPath) ? linuxPath : null,
            MountPathUNC = values.TryGetValue("MountPathUNC", out var uncPath) ? uncPath : null,
            AlreadyMounted = TryParseBool(values, "AlreadyMounted"),
            UncVerified = TryParseNullableBool(values, "UncVerified"),
            ErrorMessage = values.TryGetValue("ErrorMessage", out var error) ? error : null,
            ErrorCode = values.TryGetValue("ErrorCode", out var errorCode) ? errorCode : null,
            ErrorHint = values.TryGetValue("ErrorHint", out var errorHint) ? errorHint : null,
            DmesgSummary = values.TryGetValue("DmesgSummary", out var dmesgSummary) ? dmesgSummary : null
        };
    }

    private static bool TryParseBool(IReadOnlyDictionary<string, string> values, string key)
    {
        return values.TryGetValue(key, out var value) &&
               bool.TryParse(value, out var parsed) &&
               parsed;
    }

    private static bool? TryParseNullableBool(IReadOnlyDictionary<string, string> values, string key)
    {
        if (!values.TryGetValue(key, out var value))
        {
            return null;
        }

        return bool.TryParse(value, out var parsed) ? parsed : null;
    }
}
