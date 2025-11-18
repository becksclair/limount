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
    /// Creates an <see cref="UnmountResult"/> from a dictionary of key/value pairs produced by the unmount script.
    /// </summary>
    /// <param name="values">A map of script output keys to values (e.g., "STATUS", "DiskIndex", "ErrorMessage").</param>
    /// <returns>
    /// An <see cref="UnmountResult"/> whose <see cref="UnmountResult.Success"/> is true when the "STATUS" entry equals &quot;OK&quot;,
    /// <see cref="UnmountResult.DiskIndex"/> is parsed from "DiskIndex" or set to 0 if missing or invalid,
    /// and <see cref="UnmountResult.ErrorMessage"/> is the value of "ErrorMessage" or null if absent.
    /// </returns>
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