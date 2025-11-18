namespace LiMount.Core.Services;

/// <summary>
/// Utility for parsing key=value output from PowerShell scripts.
/// Handles STATUS=OK/ERROR patterns and extracts result data.
/// </summary>
public static class KeyValueOutputParser
{
    /// <summary>
    /// Parses lines of "key=value" format into a dictionary.
    /// Lines that don't match the pattern are ignored.
    /// Keys and values are trimmed of whitespace.
    /// </summary>
    /// <param name="output">Multi-line output from PowerShell script</param>
    /// <summary>
    /// Parses PowerShell-style "key=value" lines from the provided text into a dictionary.
    /// </summary>
    /// <param name="output">Multiline text containing lines of the form "key=value". Lines that are empty, lack a key, or do not contain '=' are ignored.</param>
    /// <returns>A dictionary mapping keys to their trimmed values; keys are compared case-insensitively and values may contain '=' characters.</returns>
    public static Dictionary<string, string> Parse(string output)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(output))
        {
            return result;
        }

        var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmedLine))
            {
                continue;
            }

            // Split on first '=' only (values may contain '=')
            var separatorIndex = trimmedLine.IndexOf('=');
            if (separatorIndex <= 0) // -1 means not found, 0 means key is empty
            {
                continue;
            }

            var key = trimmedLine.Substring(0, separatorIndex).Trim();
            var value = separatorIndex < trimmedLine.Length - 1
                ? trimmedLine.Substring(separatorIndex + 1).Trim()
                : string.Empty;

            if (!string.IsNullOrWhiteSpace(key))
            {
                result[key] = value;
            }
        }

        return result;
    }

    /// <summary>
    /// Checks if the parsed output indicates success (STATUS=OK).
    /// <summary>
    /// Determines whether the parsed output indicates a successful status.
    /// </summary>
    /// <param name="values">Parsed key-value pairs to inspect.</param>
    /// <returns>`true` if the `STATUS` entry equals `OK` (case-insensitive), `false` otherwise.</returns>
    public static bool IsSuccess(Dictionary<string, string> values)
    {
        return values.TryGetValue("STATUS", out var status) &&
               status.Equals("OK", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Gets the error message from parsed output, if any.
    /// <summary>
    /// Retrieves the parsed "ErrorMessage" value from the provided key-value dictionary.
    /// </summary>
    /// <param name="values">A dictionary of parsed key-value pairs (case-insensitive keys expected).</param>
    /// <returns>The value of "ErrorMessage" if present; otherwise <c>null</c>.</returns>
    public static string? GetErrorMessage(Dictionary<string, string> values)
    {
        return values.TryGetValue("ErrorMessage", out var error) ? error : null;
    }
}