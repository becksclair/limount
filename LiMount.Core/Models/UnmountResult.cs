namespace LiMount.Core.Models;

/// <summary>
/// Result of executing the Unmount-LinuxDisk.ps1 PowerShell script.
/// Parses key=value output from the script.
/// </summary>
public class UnmountResult
{
    /// <summary>
    /// Whether the unmount operation succeeded.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// The disk index that was unmounted.
    /// </summary>
    public int DiskIndex { get; set; }

    /// <summary>
    /// Error message if the unmount operation failed.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Creates an UnmountResult from a dictionary of key-value pairs.
    /// Expected keys: STATUS, DiskIndex, ErrorMessage
    /// <summary>
    /// Creates an UnmountResult from a dictionary of key/value pairs produced by the Unmount-LinuxDisk.ps1 script.
    /// </summary>
    /// <param name="values">Dictionary containing output keys such as "STATUS", "DiskIndex", and "ErrorMessage".</param>
    /// <returns>An UnmountResult whose Success is true when "STATUS" equals "OK", DiskIndex parsed from "DiskIndex" (defaults to 0), and ErrorMessage set from "ErrorMessage" if present.</returns>
    public static UnmountResult FromDictionary(Dictionary<string, string> values)
    {
        var success = values.TryGetValue("STATUS", out var status) && status == "OK";

        var diskIndex = 0;
        if (values.TryGetValue("DiskIndex", out var diskStr))
        {
            int.TryParse(diskStr, out diskIndex);
        }

        return new UnmountResult
        {
            Success = success,
            DiskIndex = diskIndex,
            ErrorMessage = values.TryGetValue("ErrorMessage", out var error) ? error : null
        };
    }
}