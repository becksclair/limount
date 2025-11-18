using System.Runtime.Versioning;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using LiMount.Core.Interfaces;
using LiMount.Core.Models;

namespace LiMount.Core.Services;

/// <summary>
/// Service for managing and persisting the state of active mounts.
/// Persists state to JSON file for recovery across application sessions.
/// </summary>
[SupportedOSPlatform("windows")]
public class MountStateService : IMountStateService
{
    private readonly ILogger<MountStateService> _logger;
    private readonly string _stateFilePath;
    private readonly SemaphoreSlim _fileLock = new(1, 1);

    /// <summary>
    /// Initializes a new instance of <see cref="MountStateService"/>.
    /// </summary>
    /// <param name="logger">Logger for diagnostic information.</param>
    /// <param name="stateFilePath">Optional explicit path to the state file; if null, uses %LocalAppData%\LiMount\mount-state.json</param>
    public MountStateService(ILogger<MountStateService> logger, string? stateFilePath = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _stateFilePath = stateFilePath ?? GetDefaultStateFilePath();

        // Ensure directory exists
        var directory = Path.GetDirectoryName(_stateFilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    /// <summary>
    /// Gets the default state file path: %LocalAppData%\LiMount\mount-state.json
    /// </summary>
    private static string GetDefaultStateFilePath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(appData, "LiMount", "mount-state.json");
    }

    public async Task<IReadOnlyList<ActiveMount>> GetActiveMountsAsync()
    {
        await _fileLock.WaitAsync();
        try
        {
            var mounts = await LoadMountsInternalAsync();
            return mounts.AsReadOnly();
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task RegisterMountAsync(ActiveMount mount)
    {
        if (mount == null) throw new ArgumentNullException(nameof(mount));

        await _fileLock.WaitAsync();
        try
        {
            var mounts = await LoadMountsInternalAsync();

            // Remove any existing mount for this disk
            mounts.RemoveAll(m => m.DiskIndex == mount.DiskIndex);

            // Add new mount
            mount.MountedAt = DateTime.Now;
            mount.IsVerified = true;
            mount.LastVerified = DateTime.Now;
            mounts.Add(mount);

            await SaveMountsInternalAsync(mounts);

            _logger.LogInformation(
                "Registered mount: Disk {DiskIndex} -> Drive {DriveLetter}: ({UNC})",
                mount.DiskIndex, mount.DriveLetter, mount.MountPathUNC);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task UnregisterMountAsync(int diskIndex)
    {
        await _fileLock.WaitAsync();
        try
        {
            var mounts = await LoadMountsInternalAsync();
            var removed = mounts.RemoveAll(m => m.DiskIndex == diskIndex);

            if (removed > 0)
            {
                await SaveMountsInternalAsync(mounts);
                _logger.LogInformation("Unregistered mount for disk {DiskIndex}", diskIndex);
            }
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task<ActiveMount?> GetMountForDiskAsync(int diskIndex)
    {
        await _fileLock.WaitAsync();
        try
        {
            var mounts = await LoadMountsInternalAsync();
            return mounts.FirstOrDefault(m => m.DiskIndex == diskIndex);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task<ActiveMount?> GetMountForDriveLetterAsync(char driveLetter)
    {
        await _fileLock.WaitAsync();
        try
        {
            var mounts = await LoadMountsInternalAsync();
            return mounts.FirstOrDefault(m =>
                char.ToUpperInvariant(m.DriveLetter) == char.ToUpperInvariant(driveLetter));
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task<bool> IsDiskMountedAsync(int diskIndex)
    {
        var mount = await GetMountForDiskAsync(diskIndex);
        return mount != null;
    }

    public async Task<bool> IsDriveLetterInUseAsync(char driveLetter)
    {
        var mount = await GetMountForDriveLetterAsync(driveLetter);
        return mount != null;
    }

    public async Task<IReadOnlyList<ActiveMount>> ReconcileMountStateAsync()
    {
        await _fileLock.WaitAsync();
        try
        {
            var mounts = await LoadMountsInternalAsync();
            var orphanedMounts = new List<ActiveMount>();

            foreach (var mount in mounts.ToList())
            {
                // Check if UNC path still exists
                var stillExists = !string.IsNullOrEmpty(mount.MountPathUNC) &&
                                  Directory.Exists(mount.MountPathUNC);

                if (!stillExists)
                {
                    _logger.LogWarning(
                        "Found orphaned mount: Disk {DiskIndex} -> Drive {DriveLetter}: (UNC path no longer exists)",
                        mount.DiskIndex, mount.DriveLetter);

                    orphanedMounts.Add(mount);
                    mounts.Remove(mount);
                }
                else
                {
                    mount.IsVerified = true;
                    mount.LastVerified = DateTime.Now;
                }
            }

            if (orphanedMounts.Count > 0)
            {
                await SaveMountsInternalAsync(mounts);
                _logger.LogInformation("Reconciliation removed {Count} orphaned mounts", orphanedMounts.Count);
            }

            return orphanedMounts.AsReadOnly();
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task ClearAllMountsAsync()
    {
        await _fileLock.WaitAsync();
        try
        {
            await SaveMountsInternalAsync(new List<ActiveMount>());
            _logger.LogInformation("Cleared all mount state");
        }
        finally
        {
            _fileLock.Release();
        }
    }

    /// <summary>
    /// Loads mounts from JSON file. Must be called within file lock.
    /// </summary>
    private async Task<List<ActiveMount>> LoadMountsInternalAsync()
    {
        if (!File.Exists(_stateFilePath))
        {
            return new List<ActiveMount>();
        }

        try
        {
            var json = await File.ReadAllTextAsync(_stateFilePath);
            var mounts = JsonSerializer.Deserialize<List<ActiveMount>>(json);
            return mounts ?? new List<ActiveMount>();
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse state file, starting with empty state");
            return new List<ActiveMount>();
        }
    }

    /// <summary>
    /// Saves mounts to JSON file. Must be called within file lock.
    /// </summary>
    private async Task SaveMountsInternalAsync(List<ActiveMount> mounts)
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        var json = JsonSerializer.Serialize(mounts, options);
        await File.WriteAllTextAsync(_stateFilePath, json);
    }
}
