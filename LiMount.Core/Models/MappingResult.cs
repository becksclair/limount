namespace LiMount.Core.Models;

/// <summary>
/// Result of executing the Map-WSLShareToDrive.ps1 PowerShell script.
/// Parses key=value output from the script.
/// </summary>
public class MappingResult
{
    /// <summary>
    /// Whether the drive mapping operation succeeded.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// The drive letter that was mapped (e.g., "L").
    /// </summary>
    public string? DriveLetter { get; set; }

    /// <summary>
    /// The UNC path that was mapped to the drive letter.
    /// </summary>
    public string? TargetUNC { get; set; }

    /// <summary>
    /// Error message if the mapping operation failed.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Raw stdout output from the script execution.
    /// </summary>
    public string? RawOutput { get; set; }

    /// <summary>
    /// Raw stderr output from the script execution.
    /// </summary>
    public string? RawError { get; set; }

    /// <summary>
    /// Creates a MappingResult from a dictionary of key-value pairs.
    /// Expected keys: STATUS, DriveLetter, MappedTo, ErrorMessage
    /// <summary>
    /// Create a MappingResult from key/value pairs produced by the mapping PowerShell script.
    /// </summary>
    /// <param name="values">Dictionary of keys produced by the script; keys used: "STATUS" ("OK" indicates success), "DriveLetter", "MappedTo", and "ErrorMessage".</param>
    /// <returns>A MappingResult whose Success is true when "STATUS" equals "OK", DriveLetter set from "DriveLetter" (or null), TargetUNC set from "MappedTo" (or null), and ErrorMessage set from "ErrorMessage" (or null).</returns>
    public static MappingResult FromDictionary(Dictionary<string, string> values)
    {
        var success = values.TryGetValue("STATUS", out var status) && status == "OK";

        return new MappingResult
        {
            Success = success,
            DriveLetter = values.TryGetValue("DriveLetter", out var letter) ? letter : null,
            TargetUNC = values.TryGetValue("MappedTo", out var target) ? target : null,
            ErrorMessage = values.TryGetValue("ErrorMessage", out var error) ? error : null
        };
    }
}