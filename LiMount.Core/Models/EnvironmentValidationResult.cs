namespace LiMount.Core.Models;

/// <summary>
/// Represents the result of environment validation checks.
/// </summary>
public class EnvironmentValidationResult
{
    /// <summary>
    /// Gets or sets whether the environment is valid for running LiMount.
    /// </summary>
    public bool IsValid { get; set; }

    /// <summary>
    /// Gets or sets whether WSL is installed on the system.
    /// </summary>
    public bool IsWslInstalled { get; set; }

    /// <summary>
    /// Gets or sets whether at least one WSL distro is installed.
    /// </summary>
    public bool HasWslDistro { get; set; }

    /// <summary>
    /// Gets or sets the list of installed WSL distros.
    /// </summary>
    public List<string> InstalledDistros { get; set; } = new();

    /// <summary>
    /// Gets or sets whether the Windows version is compatible.
    /// </summary>
    public bool IsWindowsVersionCompatible { get; set; }

    /// <summary>
    /// Gets or sets the detected Windows version.
    /// </summary>
    public Version? WindowsVersion { get; set; }

    /// <summary>
    /// Gets or sets whether the Windows build number is compatible.
    /// </summary>
    public bool IsWindowsBuildCompatible { get; set; }

    /// <summary>
    /// Gets or sets the detected Windows build number.
    /// </summary>
    public int WindowsBuildNumber { get; set; }

    /// <summary>
    /// Gets or sets the list of validation error messages.
    /// </summary>
    public List<string> Errors { get; set; } = new();

    /// <summary>
    /// Gets or sets the list of actionable suggestions to fix validation errors.
    /// </summary>
    public List<string> Suggestions { get; set; } = new();

    /// <summary>
    /// Creates a successful validation result.
    /// </summary>
    public static EnvironmentValidationResult Success(List<string> distros, Version windowsVersion, int buildNumber)
    {
        return new EnvironmentValidationResult
        {
            IsValid = true,
            IsWslInstalled = true,
            HasWslDistro = true,
            InstalledDistros = distros,
            IsWindowsVersionCompatible = true,
            WindowsVersion = windowsVersion,
            IsWindowsBuildCompatible = true,
            WindowsBuildNumber = buildNumber
        };
    }

    /// <summary>
    /// Creates a failed validation result with errors and suggestions.
    /// </summary>
    public static EnvironmentValidationResult Failure(List<string> errors, List<string> suggestions)
    {
        return new EnvironmentValidationResult
        {
            IsValid = false,
            Errors = errors,
            Suggestions = suggestions
        };
    }
}
