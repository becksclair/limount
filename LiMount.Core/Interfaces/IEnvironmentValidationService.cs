using LiMount.Core.Models;

namespace LiMount.Core.Interfaces;

/// <summary>
/// Service for validating that the environment meets all requirements for running LiMount.
/// </summary>
public interface IEnvironmentValidationService
{
    /// <summary>
    /// Validates the current environment to ensure all prerequisites are met.
    /// </summary>
    /// <returns>
    /// A validation result indicating whether the environment is valid, along with
    /// detailed information about what checks passed or failed and actionable suggestions
    /// for fixing any issues.
    /// </returns>
    Task<EnvironmentValidationResult> ValidateEnvironmentAsync();

    /// <summary>
    /// Checks if WSL is installed and available on the system.
    /// </summary>
    /// <returns>True if WSL is available, false otherwise.</returns>
    Task<bool> IsWslInstalledAsync();

    /// <summary>
    /// Gets the list of installed WSL distributions.
    /// </summary>
    /// <returns>A list of distribution names, or an empty list if none are found.</returns>
    Task<List<string>> GetInstalledDistrosAsync();

    /// <summary>
    /// Checks if the Windows version is compatible with LiMount.
    /// </summary>
    /// <returns>True if the Windows version is compatible, false otherwise.</returns>
    bool IsWindowsVersionCompatible();

    /// <summary>
    /// Gets the current Windows version.
    /// </summary>
    /// <returns>The Windows version, or null if it cannot be determined.</returns>
    Version? GetWindowsVersion();

    /// <summary>
    /// Gets the current Windows build number.
    /// </summary>
    /// <returns>The Windows build number, or 0 if it cannot be determined.</returns>
    int GetWindowsBuildNumber();
}
