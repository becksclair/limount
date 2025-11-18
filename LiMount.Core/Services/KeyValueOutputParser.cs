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
    /// <returns>Dictionary of key-value pairs</returns>
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
    /// </summary>
    public static bool IsSuccess(Dictionary<string, string> values)
    {
        return values.TryGetValue("STATUS", out var status) &&
               status.Equals("OK", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Gets the error message from parsed output, if any.
    /// </summary>
    public static string? GetErrorMessage(Dictionary<string, string> values)
    {
        return values.TryGetValue("ErrorMessage", out var error) ? error : null;
    }
}
