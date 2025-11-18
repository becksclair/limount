namespace LiMount.Core.Models;

/// <summary>
/// Result of executing the Unmap-DriveLetter.ps1 PowerShell script.
/// Parses key=value output from the script.
/// </summary>
public class UnmappingResult
{
    /// <summary>
    /// Whether the unmapping operation succeeded.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// The drive letter that was unmapped.
    /// </summary>
    public string? DriveLetter { get; set; }

    /// <summary>
    /// Error message if the unmapping operation failed.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Creates an UnmappingResult from a dictionary of key-value pairs.
    /// Expected keys: STATUS, DriveLetter, ErrorMessage
    /// </summary>
    public static UnmappingResult FromDictionary(Dictionary<string, string> values)
    {
        var success = values.TryGetValue("STATUS", out var status) && status == "OK";

        return new UnmappingResult
        {
            Success = success,
            DriveLetter = values.TryGetValue("DriveLetter", out var letter) ? letter : null,
            ErrorMessage = values.TryGetValue("ErrorMessage", out var error) ? error : null
        };
    }
}
