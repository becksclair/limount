using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using LiMount.Core.Configuration;
using LiMount.Core.Interfaces;
using LiMount.Core.Models;
using LiMount.Core.Serialization;

namespace LiMount.Core.Services;

/// <summary>
/// Service for managing and persisting the state of active mounts.
/// Persists state to JSON file for recovery across application sessions.
/// </summary>
[SupportedOSPlatform("windows")]
public class MountStateService : IMountStateService, IDisposable
{
    private readonly ILogger<MountStateService> _logger;
    private readonly IDriveLetterService _driveLetterService;
    private readonly string _stateFilePath;
    private readonly SemaphoreSlim _fileLock = new(1, 1);
    private readonly int _reconcileUncCheckTimeoutMs;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of <see cref="MountStateService"/>.
    /// </summary>
    /// <param name="logger">Logger for diagnostic information.</param>
    /// <param name="driveLetterService">Service for managing drive letters.</param>
    /// <param name="configuration">Configuration options for LiMount.</param>
    /// <param name="stateFilePath">Optional explicit path to the state file. If null, uses configuration.Value.History.StateFilePath if present, otherwise uses the default path %LocalAppData%\LiMount\mount-state.json.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="logger"/>, <paramref name="driveLetterService"/>, or <paramref name="configuration"/> is null.</exception>
    /// <remarks>
    /// The state file directory is created if it is missing.
    /// </remarks>
    public MountStateService(
        ILogger<MountStateService> logger,
        IDriveLetterService driveLetterService,
        IOptions<LiMountConfiguration> configuration,
        string? stateFilePath = null)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(driveLetterService);
        ArgumentNullException.ThrowIfNull(configuration);

        _logger = logger;
        _driveLetterService = driveLetterService;

        var config = configuration.Value;

        _reconcileUncCheckTimeoutMs = Math.Clamp(
            config.MountOperations.ReconcileUncAccessibilityTimeoutMs,
            500,
            10000);

        var configuredPath = stateFilePath ?? config.History.StateFilePath;
        _stateFilePath = !string.IsNullOrEmpty(configuredPath)
            ? configuredPath
            : GetDefaultStateFilePath();

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

    public async Task<IReadOnlyList<ActiveMount>> GetActiveMountsAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(MountStateService));

        bool acquired = false;
        try
        {
            await _fileLock.WaitAsync(cancellationToken);
            acquired = true;
            var mounts = await LoadMountsInternalAsync(cancellationToken);
            return mounts.AsReadOnly();
        }
        finally
        {
            if (acquired) _fileLock.Release();
        }
    }

    public async Task RegisterMountAsync(ActiveMount mount, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(MountStateService));

        if (mount == null) throw new ArgumentNullException(nameof(mount));

        bool acquired = false;
        try
        {
            await _fileLock.WaitAsync(cancellationToken);
            acquired = true;
            var mounts = await LoadMountsInternalAsync(cancellationToken);

            // Remove any existing mount for this specific partition (allows multi-partition per disk)
            mounts.RemoveAll(m => m.DiskIndex == mount.DiskIndex && m.PartitionNumber == mount.PartitionNumber);

            // Create a copy of the mount with timestamps and verification status
            var mountCopy = new ActiveMount
            {
                Id = mount.Id,
                DiskIndex = mount.DiskIndex,
                PartitionNumber = mount.PartitionNumber,
                AccessMode = mount.AccessMode,
                DriveLetter = mount.DriveLetter,
                NetworkLocationName = mount.NetworkLocationName,
                DistroName = mount.DistroName,
                MountPathLinux = mount.MountPathLinux,
                MountPathUNC = mount.MountPathUNC,
                MountedAt = DateTime.UtcNow,
                IsVerified = true,
                LastVerified = DateTime.UtcNow
            };

            // Add new mount
            mounts.Add(mountCopy);

            await SaveMountsInternalAsync(mounts, cancellationToken);

            _logger.LogInformation(
                "Registered mount: Disk {DiskIndex} Partition {Partition} Mode {AccessMode} Drive {DriveLetter} NetworkLocation {NetworkLocationName} ({UNC})",
                mountCopy.DiskIndex,
                mountCopy.PartitionNumber,
                mountCopy.AccessMode,
                mountCopy.DriveLetter?.ToString() ?? "(none)",
                mountCopy.NetworkLocationName ?? "(none)",
                mountCopy.MountPathUNC);
        }
        finally
        {
            if (acquired) _fileLock.Release();
        }
    }

    [Obsolete("Use UnregisterDiskAsync for disk-wide removal or UnregisterMountAsync(diskIndex, partition) for partition-scoped removal.")]
    public async Task UnregisterMountAsync(int diskIndex, CancellationToken cancellationToken = default)
    {
        await UnregisterDiskAsync(diskIndex, cancellationToken);
    }

    public async Task UnregisterMountAsync(int diskIndex, int partition, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(MountStateService));

        bool acquired = false;
        try
        {
            await _fileLock.WaitAsync(cancellationToken);
            acquired = true;
            var mounts = await LoadMountsInternalAsync(cancellationToken);
            var removed = mounts.RemoveAll(m => m.DiskIndex == diskIndex && m.PartitionNumber == partition);

            if (removed > 0)
            {
                await SaveMountsInternalAsync(mounts, cancellationToken);
                _logger.LogInformation("Unregistered mount for disk {DiskIndex} partition {Partition}", diskIndex, partition);
            }
        }
        finally
        {
            if (acquired) _fileLock.Release();
        }
    }

    public async Task UnregisterDiskAsync(int diskIndex, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(MountStateService));

        bool acquired = false;
        try
        {
            await _fileLock.WaitAsync(cancellationToken);
            acquired = true;
            var mounts = await LoadMountsInternalAsync(cancellationToken);
            var removed = mounts.RemoveAll(m => m.DiskIndex == diskIndex);

            if (removed > 0)
            {
                await SaveMountsInternalAsync(mounts, cancellationToken);
                _logger.LogInformation("Unregistered {Count} mount(s) for disk {DiskIndex}", removed, diskIndex);
            }
        }
        finally
        {
            if (acquired) _fileLock.Release();
        }
    }

    public async Task<ActiveMount?> GetMountForDiskAsync(int diskIndex, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(MountStateService));

        bool acquired = false;
        try
        {
            await _fileLock.WaitAsync(cancellationToken);
            acquired = true;
            var mounts = await LoadMountsInternalAsync(cancellationToken);
            return mounts.FirstOrDefault(m => m.DiskIndex == diskIndex);
        }
        finally
        {
            if (acquired) _fileLock.Release();
        }
    }

    public async Task<ActiveMount?> GetMountForDiskPartitionAsync(int diskIndex, int partition, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(MountStateService));

        bool acquired = false;
        try
        {
            await _fileLock.WaitAsync(cancellationToken);
            acquired = true;
            var mounts = await LoadMountsInternalAsync(cancellationToken);
            return mounts.FirstOrDefault(m => m.DiskIndex == diskIndex && m.PartitionNumber == partition);
        }
        finally
        {
            if (acquired) _fileLock.Release();
        }
    }

    public async Task<IReadOnlyList<ActiveMount>> GetMountsForDiskAsync(int diskIndex, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(MountStateService));

        bool acquired = false;
        try
        {
            await _fileLock.WaitAsync(cancellationToken);
            acquired = true;
            var mounts = await LoadMountsInternalAsync(cancellationToken);
            return mounts.Where(m => m.DiskIndex == diskIndex).ToList().AsReadOnly();
        }
        finally
        {
            if (acquired) _fileLock.Release();
        }
    }

    public async Task<ActiveMount?> GetMountForDriveLetterAsync(char driveLetter, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(MountStateService));

        bool acquired = false;
        try
        {
            await _fileLock.WaitAsync(cancellationToken);
            acquired = true;
            var mounts = await LoadMountsInternalAsync(cancellationToken);
            return mounts.FirstOrDefault(m =>
                m.DriveLetter.HasValue &&
                char.ToUpperInvariant(m.DriveLetter.Value) == char.ToUpperInvariant(driveLetter));
        }
        finally
        {
            if (acquired) _fileLock.Release();
        }
    }

    public async Task<bool> IsDiskMountedAsync(int diskIndex, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(MountStateService));

        var mounts = await GetMountsForDiskAsync(diskIndex, cancellationToken);
        return mounts.Count > 0;
    }

    public async Task<bool> IsDriveLetterInUseAsync(char driveLetter, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(MountStateService));

        var mount = await GetMountForDriveLetterAsync(driveLetter, cancellationToken);
        return mount != null;
    }

    public async Task<IReadOnlyList<ActiveMount>> ReconcileMountStateAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(MountStateService));

        bool acquired = false;
        try
        {
            await _fileLock.WaitAsync(cancellationToken);
            acquired = true;
            var mounts = await LoadMountsInternalAsync(cancellationToken);
            var orphanedMounts = new List<ActiveMount>();
            var validMounts = new List<ActiveMount>();

            var usedDriveLetters = new HashSet<char>(_driveLetterService.GetUsedLetters()
                .Select(char.ToUpperInvariant));

            // Phase 1: Quick synchronous check for mode-specific validity
            foreach (var mount in mounts)
            {
                if (mount.AccessMode == WindowsAccessMode.DriveLetterLegacy)
                {
                    var normalizedLetter = mount.DriveLetter.HasValue
                        ? char.ToUpperInvariant(mount.DriveLetter.Value)
                        : '\0';
                    var hasValidLetter = normalizedLetter >= 'A' && normalizedLetter <= 'Z';
                    var driveStillMapped = hasValidLetter && usedDriveLetters.Contains(normalizedLetter);

                    if (!driveStillMapped)
                    {
                        _logger.LogWarning(
                            "Found orphaned legacy mount: Disk {DiskIndex} Partition {Partition} Drive {DriveLetter} (drive letter no longer mapped)",
                            mount.DiskIndex,
                            mount.PartitionNumber,
                            mount.DriveLetter?.ToString() ?? "(none)");
                        orphanedMounts.Add(mount);
                    }
                    else
                    {
                        validMounts.Add(mount);
                    }
                }
                else
                {
                    validMounts.Add(mount);
                }
            }

            // Phase 2: Parallel UNC path verification for valid mounts
            // Track verification results to properly set IsVerified flag
            var uncVerificationResults = new Dictionary<string, bool>(); // MountId/unique key -> accessible

            var uncVerificationTasks = validMounts
                .Where(m => !string.IsNullOrEmpty(m.MountPathUNC))
                .Select(async mount =>
                {
                    var accessible = await TryVerifyUncAccessibilityAsync(mount.MountPathUNC!);
                    return (mount, accessible);
                })
                .ToList();

            if (uncVerificationTasks.Count > 0)
            {
                var results = await Task.WhenAll(uncVerificationTasks);

                foreach (var (mount, accessible) in results)
                {
                    uncVerificationResults[GetVerificationKey(mount)] = accessible;

                    if (!accessible)
                    {
                        _logger.LogWarning(
                            "UNC path {UNC} for Disk {DiskIndex} is temporarily inaccessible; drive {DriveLetter} is still mapped.",
                            mount.MountPathUNC,
                            mount.DiskIndex,
                            mount.DriveLetter?.ToString() ?? "(none)");
                }
            }
            }

            // Phase 3: Update verified status based on actual verification results
            // - Mounts with UNC paths: verified only if UNC is accessible
            // - Mounts without UNC paths: verified based on drive letter check (already passed Phase 1)
            var now = DateTime.UtcNow;
            foreach (var mount in validMounts)
            {
                if (!string.IsNullOrEmpty(mount.MountPathUNC))
                {
                    // Has UNC path - use verification result
                    mount.IsVerified = uncVerificationResults.TryGetValue(
                        GetVerificationKey(mount),
                        out var accessible) && accessible;
                }
                else
                {
                    // No UNC path - drive letter check in Phase 1 is the only verification possible
                    mount.IsVerified = true;
                }
                mount.LastVerified = now;
            }

            if (orphanedMounts.Count > 0)
            {
                await SaveMountsInternalAsync(validMounts, cancellationToken);
                _logger.LogInformation("Reconciliation removed {Count} orphaned mounts", orphanedMounts.Count);
            }

            return orphanedMounts.AsReadOnly();
        }
        finally
        {
            if (acquired) _fileLock.Release();
        }
    }

    public async Task ClearAllMountsAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(MountStateService));

        bool acquired = false;
        try
        {
            await _fileLock.WaitAsync(cancellationToken);
            acquired = true;
            await SaveMountsInternalAsync(new List<ActiveMount>(), cancellationToken);
            _logger.LogInformation("Cleared all mount state");
        }
        finally
        {
            if (acquired) _fileLock.Release();
        }
    }

    /// <summary>
    /// Loads mounts from JSON file. Must be called within file lock.
    /// </summary>
    private async Task<List<ActiveMount>> LoadMountsInternalAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_stateFilePath))
        {
            return new List<ActiveMount>();
        }

        try
        {
            var json = await File.ReadAllTextAsync(_stateFilePath, cancellationToken);
            var mounts = JsonSerializer.Deserialize(
                json,
                LiMountJsonContext.Default.ListActiveMount) ?? new List<ActiveMount>();
            return mounts;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse state file, starting with empty state");
            return new List<ActiveMount>();
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Failed to read state file due to IO error, starting with empty state");
            return new List<ActiveMount>();
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Failed to access state file due to permissions, starting with empty state");
            return new List<ActiveMount>();
        }
    }

    /// <summary>
    /// Saves mounts to JSON file. Must be called within file lock.
    /// </summary>
    private async Task SaveMountsInternalAsync(List<ActiveMount> mounts, CancellationToken cancellationToken = default)
    {
        try
        {
            var json = JsonSerializer.Serialize(
                mounts,
                LiMountJsonContext.Default.ListActiveMount);
            await File.WriteAllTextAsync(_stateFilePath, json, cancellationToken);
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "Failed to save state file due to IO error");
            throw; // Re-throw for save operations as the caller should handle the failure
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogError(ex, "Failed to save state file due to permissions");
            throw; // Re-throw for save operations as the caller should handle the failure
        }
    }

    private static string GetVerificationKey(ActiveMount mount)
    {
        if (!string.IsNullOrWhiteSpace(mount.Id))
        {
            return mount.Id;
        }

        return $"{mount.DiskIndex}:{mount.PartitionNumber}";
    }

    private async Task<bool> TryVerifyUncAccessibilityAsync(string uncPath)
    {
        try
        {
            var checkTask = Task.Run(() => Directory.Exists(uncPath));
            var completedTask = await Task.WhenAny(checkTask, Task.Delay(_reconcileUncCheckTimeoutMs));
            if (completedTask == checkTask)
            {
                // Use await instead of .Result to avoid potential deadlock in WPF sync context
                return await checkTask;
            }

            _logger.LogWarning(
                "UNC verification for {UNC} timed out after {TimeoutMs}ms",
                uncPath,
                _reconcileUncCheckTimeoutMs);

            return false;
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "IO error verifying UNC accessibility for {UNC}", uncPath);
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Access denied verifying UNC accessibility for {UNC}", uncPath);
            return false;
        }
    }

    /// <summary>
    /// Disposes the service and releases managed resources.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Disposes the service and releases managed resources.
    /// </summary>
    /// <param name="disposing">True if called from Dispose(), false if called from finalizer.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                _fileLock?.Dispose();
            }
            _disposed = true;
        }
    }
}
