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
    /// Creates a MappingResult from a dictionary of key-value pairs.
    /// Expected keys: STATUS, DriveLetter, MappedTo, ErrorMessage
    /// <summary>
    /// Create a MappingResult from a dictionary of key/value pairs produced by the Map-WSLShareToDrive.ps1 script.
    /// </summary>
    /// <param name="values">Dictionary of output keys to values. Expected keys include "STATUS" (value "OK" indicates success), "DriveLetter", "MappedTo" and "ErrorMessage".</param>
    /// <returns>A MappingResult with Success set based on "STATUS", DriveLetter from "DriveLetter", TargetUNC from "MappedTo", and ErrorMessage from "ErrorMessage". Missing keys result in null properties.</returns>
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