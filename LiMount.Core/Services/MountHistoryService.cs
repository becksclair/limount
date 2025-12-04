using System.Runtime.Versioning;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using LiMount.Core.Interfaces;
using LiMount.Core.Models;
using LiMount.Core.Configuration;

namespace LiMount.Core.Services;

/// <summary>
/// Service for tracking and persisting mount/unmount operation history to JSON file.
/// </summary>
/// <remarks>
/// This class is thread-safe. All public methods use internal locking to ensure
/// consistency when accessed from multiple threads.
/// </remarks>
[SupportedOSPlatform("windows")]
public class MountHistoryService : IMountHistoryService, IDisposable
{
    /// <summary>
    /// Maximum allowed history entries to prevent memory exhaustion from misconfiguration.
    /// </summary>
    private const int MaxAllowedEntries = 10000;

    private readonly ILogger<MountHistoryService> _logger;
    private readonly string _historyFilePath;
    private readonly int _maxEntries;
    private readonly SemaphoreSlim _fileLock = new(1, 1);
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of <see cref="MountHistoryService"/>.
    /// </summary>
    /// <param name="logger">Logger for diagnostic information.</param>
    /// <param name="config">Optional configuration containing history settings.</param>
    /// <param name="historyFilePath">Optional explicit path to the history file; if null, uses %LocalAppData%\LiMount\mount-history.json</param>
    public MountHistoryService(
        ILogger<MountHistoryService> logger,
        IOptions<LiMountConfiguration>? config = null,
        string? historyFilePath = null)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
        _historyFilePath = historyFilePath ?? GetDefaultHistoryFilePath();
        // Clamp maxEntries to reasonable range (1-10000) to prevent memory exhaustion
        _maxEntries = Math.Clamp(config?.Value?.History?.MaxEntries ?? 100, 1, MaxAllowedEntries);

        // Ensure directory exists
        var directory = Path.GetDirectoryName(_historyFilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    /// <summary>
    /// Throws ObjectDisposedException if this instance has been disposed.
    /// </summary>
    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(MountHistoryService));
        }
    }

    /// <summary>
    /// Gets the default history file path: %LocalAppData%\LiMount\mount-history.json
    /// </summary>
    private static string GetDefaultHistoryFilePath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(appData, "LiMount", "mount-history.json");
    }

    /// <summary>
    /// Adds a history entry for a mount or unmount operation.
    /// </summary>
    public async Task AddEntryAsync(MountHistoryEntry entry)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(entry);

        await _fileLock.WaitAsync();
        try
        {
            var history = await LoadHistoryInternalAsync();

            // Trim BEFORE adding to prevent temporary memory overflow
            // Keep only the last (maxEntries - 1) entries to make room for the new one
            if (history.Count >= _maxEntries)
            {
                history = history.OrderByDescending(e => e.Timestamp).Take(_maxEntries - 1).ToList();
            }

            history.Add(entry);

            await SaveHistoryInternalAsync(history);

            _logger.LogInformation("Added history entry: {OperationType} for disk {DiskIndex} (Success: {Success})",
                entry.OperationType, entry.DiskIndex, entry.Success);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add history entry");
        }
        finally
        {
            _fileLock.Release();
        }
    }

    /// <summary>
    /// Gets all history entries, ordered by timestamp (newest first).
    /// </summary>
    public async Task<IReadOnlyList<MountHistoryEntry>> GetHistoryAsync()
    {
        ThrowIfDisposed();

        await _fileLock.WaitAsync();
        try
        {
            var history = await LoadHistoryInternalAsync();
            return history.OrderByDescending(e => e.Timestamp).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load history");
            return new List<MountHistoryEntry>();
        }
        finally
        {
            _fileLock.Release();
        }
    }

    /// <summary>
    /// Clears all history entries.
    /// </summary>
    public async Task ClearHistoryAsync()
    {
        ThrowIfDisposed();

        await _fileLock.WaitAsync();
        try
        {
            await SaveHistoryInternalAsync(new List<MountHistoryEntry>());
            _logger.LogInformation("Cleared mount history");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clear history");
        }
        finally
        {
            _fileLock.Release();
        }
    }

    /// <summary>
    /// Gets the most recent mount operation for a specific disk, if any.
    /// </summary>
    public async Task<MountHistoryEntry?> GetLastMountForDiskAsync(int diskIndex)
    {
        ThrowIfDisposed();

        await _fileLock.WaitAsync();
        try
        {
            var history = await LoadHistoryInternalAsync();

            // Single-pass iteration to find the most recent matching entry (O(n) instead of O(n log n))
            MountHistoryEntry? lastMount = null;
            DateTime? latestTimestamp = null;

            foreach (var entry in history)
            {
                if (entry.DiskIndex == diskIndex &&
                    entry.OperationType == MountHistoryOperationType.Mount &&
                    entry.Success &&
                    (latestTimestamp == null || entry.Timestamp > latestTimestamp))
                {
                    lastMount = entry;
                    latestTimestamp = entry.Timestamp;
                }
            }

            return lastMount;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get last mount for disk {DiskIndex}", diskIndex);
            return null;
        }
        finally
        {
            _fileLock.Release();
        }
    }

    /// <summary>
    /// Loads history from JSON file. Must be called within file lock.
    /// </summary>
    private async Task<List<MountHistoryEntry>> LoadHistoryInternalAsync()
    {
        if (!File.Exists(_historyFilePath))
        {
            return new List<MountHistoryEntry>();
        }

        try
        {
            var json = await File.ReadAllTextAsync(_historyFilePath);
            var history = JsonSerializer.Deserialize<List<MountHistoryEntry>>(json);
            return history ?? new List<MountHistoryEntry>();
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse history file, starting with empty history");
            return new List<MountHistoryEntry>();
        }
    }

    /// <summary>
    /// Saves history to JSON file. Must be called within file lock.
    /// </summary>
    private async Task SaveHistoryInternalAsync(List<MountHistoryEntry> history)
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        var json = JsonSerializer.Serialize(history, options);
        await File.WriteAllTextAsync(_historyFilePath, json);
    }

    /// <summary>
    /// Releases all resources used by the <see cref="MountHistoryService"/>.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Releases resources used by the <see cref="MountHistoryService"/>.
    /// </summary>
    /// <param name="disposing">True if called from Dispose(), false if from finalizer.</param>
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
