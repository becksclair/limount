using System.Runtime.Versioning;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using LiMount.Core.Interfaces;
using LiMount.Core.Models;

namespace LiMount.Core.Services;

/// <summary>
/// Service for tracking and persisting mount/unmount operation history to JSON file.
/// </summary>
[SupportedOSPlatform("windows")]
public class MountHistoryService : IMountHistoryService
{
    private readonly ILogger<MountHistoryService> _logger;
    private readonly string _historyFilePath;
    private readonly SemaphoreSlim _fileLock = new(1, 1);

    /// <summary>
    /// Initializes a new instance of <see cref="MountHistoryService"/>.
    /// </summary>
    /// <param name="logger">Logger for diagnostic information.</param>
    /// <param name="historyFilePath">Optional explicit path to the history file; if null, uses %LocalAppData%\LiMount\mount-history.json</param>
    public MountHistoryService(ILogger<MountHistoryService> logger, string? historyFilePath = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _historyFilePath = historyFilePath ?? GetDefaultHistoryFilePath();

        // Ensure directory exists
        var directory = Path.GetDirectoryName(_historyFilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
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
        await _fileLock.WaitAsync();
        try
        {
            var history = await LoadHistoryInternalAsync();
            history.Add(entry);

            // Keep only the last 100 entries to prevent unbounded growth
            if (history.Count > 100)
            {
                history = history.OrderByDescending(e => e.Timestamp).Take(100).ToList();
            }

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
        await _fileLock.WaitAsync();
        try
        {
            var history = await LoadHistoryInternalAsync();
            return history
                .Where(e => e.DiskIndex == diskIndex && e.OperationType == "Mount" && e.Success)
                .OrderByDescending(e => e.Timestamp)
                .FirstOrDefault();
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
}
