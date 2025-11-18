using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using LiMount.Core.Interfaces;
using LiMount.Core.Models;

namespace LiMount.Core.Services;

/// <summary>
/// Service for validating that the environment meets all requirements for running LiMount.
/// </summary>
/// <remarks>
/// Checks for WSL installation, WSL distros, and Windows version compatibility.
/// Provides actionable suggestions when validation fails.
/// </remarks>
[SupportedOSPlatform("windows")]
public class EnvironmentValidationService : IEnvironmentValidationService
{
    private const int MinimumWindowsBuild = 19041; // Windows 10 Build 19041 (2004) minimum for WSL2
    private const int RecommendedWindowsBuild = 22000; // Windows 11 for best experience

    private readonly ILogger<EnvironmentValidationService>? _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="EnvironmentValidationService"/>.
    /// </summary>
    /// <param name="logger">Optional logger for diagnostic information.</param>
    public EnvironmentValidationService(ILogger<EnvironmentValidationService>? logger = null)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<EnvironmentValidationResult> ValidateEnvironmentAsync()
    {
        _logger?.LogInformation("Starting environment validation");

        var errors = new List<string>();
        var suggestions = new List<string>();

        // Check Windows version
        var windowsVersion = GetWindowsVersion();
        var buildNumber = GetWindowsBuildNumber();
        var isWindowsCompatible = IsWindowsVersionCompatible();
        var isBuildCompatible = buildNumber >= MinimumWindowsBuild;

        _logger?.LogInformation("Windows version: {Version}, Build: {Build}", windowsVersion, buildNumber);

        if (!isWindowsCompatible || !isBuildCompatible)
        {
            errors.Add($"Windows version {windowsVersion} (Build {buildNumber}) is not compatible with LiMount.");
            suggestions.Add($"LiMount requires Windows 10 Build {MinimumWindowsBuild} (Version 2004) or later.");
            suggestions.Add($"Windows 11 Build {RecommendedWindowsBuild}+ is recommended for the best experience.");
            suggestions.Add("Please update your Windows installation through Windows Update.");

            _logger?.LogWarning("Windows version is not compatible. Version: {Version}, Build: {Build}", windowsVersion, buildNumber);

            return EnvironmentValidationResult.Failure(errors, suggestions);
        }

        // Check WSL installation
        var isWslInstalled = await IsWslInstalledAsync();

        if (!isWslInstalled)
        {
            errors.Add("WSL (Windows Subsystem for Linux) is not installed on this system.");
            suggestions.Add("Install WSL by running the following command in PowerShell as Administrator:");
            suggestions.Add("    wsl --install");
            suggestions.Add("After installation, restart your computer and install a Linux distribution from the Microsoft Store.");
            suggestions.Add("For more information, visit: https://docs.microsoft.com/en-us/windows/wsl/install");

            _logger?.LogWarning("WSL is not installed");

            return EnvironmentValidationResult.Failure(errors, suggestions);
        }

        // Check for installed distros
        var distros = await GetInstalledDistrosAsync();

        if (distros.Count == 0)
        {
            errors.Add("No WSL distributions are installed.");
            suggestions.Add("Install a Linux distribution from the Microsoft Store (e.g., Ubuntu, Debian).");
            suggestions.Add("Alternatively, use the command: wsl --install -d Ubuntu");
            suggestions.Add("After installation, launch the distribution to complete its setup.");

            _logger?.LogWarning("No WSL distributions found");

            return EnvironmentValidationResult.Failure(errors, suggestions);
        }

        _logger?.LogInformation("Environment validation successful. Found {Count} WSL distro(s): {Distros}",
            distros.Count, string.Join(", ", distros));

        return EnvironmentValidationResult.Success(distros, windowsVersion ?? new Version(10, 0), buildNumber);
    }

    /// <inheritdoc/>
    public async Task<bool> IsWslInstalledAsync()
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "wsl.exe",
                Arguments = "--status",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process == null)
            {
                _logger?.LogWarning("Failed to start wsl.exe process");
                return false;
            }

            await process.WaitForExitAsync();

            // If wsl.exe exists and runs without error, WSL is installed
            return process.ExitCode == 0;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Error checking WSL installation");
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task<List<string>> GetInstalledDistrosAsync()
    {
        var distros = new List<string>();

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "wsl.exe",
                Arguments = "--list --quiet",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = System.Text.Encoding.Unicode // WSL outputs in Unicode
            };

            using var process = Process.Start(startInfo);
            if (process == null)
            {
                _logger?.LogWarning("Failed to start wsl.exe process for listing distros");
                return distros;
            }

            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                _logger?.LogWarning("wsl --list --quiet returned non-zero exit code: {ExitCode}", process.ExitCode);
                return distros;
            }

            // Parse the output - each line is a distro name
            var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                // Remove the (Default) marker if present
                if (trimmed.EndsWith("(Default)", StringComparison.OrdinalIgnoreCase))
                {
                    trimmed = trimmed.Replace("(Default)", "").Trim();
                }

                if (!string.IsNullOrWhiteSpace(trimmed))
                {
                    distros.Add(trimmed);
                    _logger?.LogDebug("Found WSL distro: {Distro}", trimmed);
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error getting installed WSL distros");
        }

        return distros;
    }

    /// <inheritdoc/>
    public bool IsWindowsVersionCompatible()
    {
        var buildNumber = GetWindowsBuildNumber();
        return buildNumber >= MinimumWindowsBuild;
    }

    /// <inheritdoc/>
    public Version? GetWindowsVersion()
    {
        try
        {
            return Environment.OSVersion.Version;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to get Windows version");
            return null;
        }
    }

    /// <inheritdoc/>
    public int GetWindowsBuildNumber()
    {
        try
        {
            // Try to get build number from registry (most reliable)
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            if (key != null)
            {
                var buildValue = key.GetValue("CurrentBuildNumber");
                if (buildValue != null && int.TryParse(buildValue.ToString(), out var buildNumber))
                {
                    return buildNumber;
                }
            }

            // Fallback to Environment.OSVersion.Version.Build
            return Environment.OSVersion.Version.Build;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to get Windows build number");
            return 0;
        }
    }
}
