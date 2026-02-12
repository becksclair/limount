using System.Runtime.Versioning;
using System.Text.Json;
using LiMount.Core.Interfaces;
using LiMount.Core.Models;
using LiMount.Core.Serialization;
using Microsoft.Extensions.Logging;

namespace LiMount.Core.Services;

/// <summary>
/// JSON-backed user settings persistence service.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class UserSettingsService : IUserSettingsService, IDisposable
{
    private readonly ILogger<UserSettingsService>? _logger;
    private readonly string _settingsFilePath;
    private readonly SemaphoreSlim _fileLock = new(1, 1);
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of <see cref="UserSettingsService"/>.
    /// </summary>
    public UserSettingsService(
        ILogger<UserSettingsService>? logger = null,
        string? settingsFilePath = null)
    {
        _logger = logger;
        _settingsFilePath = !string.IsNullOrWhiteSpace(settingsFilePath)
            ? settingsFilePath
            : GetDefaultSettingsFilePath();

        var directory = Path.GetDirectoryName(_settingsFilePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    public async Task<UserSettings> LoadOrCreateAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        bool acquired = false;
        try
        {
            await _fileLock.WaitAsync(cancellationToken);
            acquired = true;

            if (!File.Exists(_settingsFilePath))
            {
                return CreateDefaults();
            }

            try
            {
                var json = await File.ReadAllTextAsync(_settingsFilePath, cancellationToken);
                var settings = JsonSerializer.Deserialize(
                    json,
                    LiMountJsonContext.Default.UserSettings);

                return Normalize(settings);
            }
            catch (JsonException ex)
            {
                _logger?.LogWarning(ex, "Settings file is invalid JSON. Returning defaults.");
                return CreateDefaults();
            }
            catch (IOException ex)
            {
                _logger?.LogWarning(ex, "Settings file could not be read. Returning defaults.");
                return CreateDefaults();
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger?.LogWarning(ex, "Settings file access denied. Returning defaults.");
                return CreateDefaults();
            }
        }
        finally
        {
            if (acquired)
            {
                _fileLock.Release();
            }
        }
    }

    public async Task SaveAsync(UserSettings settings, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(settings);

        bool acquired = false;
        try
        {
            await _fileLock.WaitAsync(cancellationToken);
            acquired = true;

            var normalized = Normalize(settings);
            var json = JsonSerializer.Serialize(
                normalized,
                LiMountJsonContext.Default.UserSettings);

            await File.WriteAllTextAsync(_settingsFilePath, json, cancellationToken);
        }
        finally
        {
            if (acquired)
            {
                _fileLock.Release();
            }
        }
    }

    public async Task ResetAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        bool acquired = false;
        try
        {
            await _fileLock.WaitAsync(cancellationToken);
            acquired = true;

            if (File.Exists(_settingsFilePath))
            {
                File.Delete(_settingsFilePath);
            }
        }
        finally
        {
            if (acquired)
            {
                _fileLock.Release();
            }
        }
    }

    private static string GetDefaultSettingsFilePath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(appData, "LiMount", "settings.json");
    }

    private static UserSettings CreateDefaults() => Normalize(new UserSettings());

    private static UserSettings Normalize(UserSettings? settings)
    {
        settings ??= new UserSettings();

        if (settings.Version < 1)
        {
            settings.Version = 1;
        }

        settings.VmAppliance ??= new VmApplianceSettings();
        settings.GuestAuth ??= new GuestAuthSettings();

        return settings;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(UserSettingsService));
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _fileLock.Dispose();
        _disposed = true;
    }
}
