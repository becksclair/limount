using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using LiMount.App.Services;
using LiMount.Core.Interfaces;
using LiMount.Core.Models;

namespace LiMount.App.ViewModels;

/// <summary>
/// ViewModel for the History Window, displaying mount/unmount operation history.
/// </summary>
public partial class HistoryViewModel : ObservableObject
{
    private readonly IMountHistoryService _historyService;
    private readonly IDialogService _dialogService;
    private readonly ILogger<HistoryViewModel> _logger;

    [ObservableProperty]
    private ObservableCollection<MountHistoryEntryDisplay> _historyEntries = new();

    [ObservableProperty]
    private string _statusMessage = "Loading history...";

    /// <summary>
    /// Initializes a new instance of HistoryViewModel.
    /// </summary>
    /// <param name="historyService">Service to retrieve mount history.</param>
    /// <param name="dialogService">Service to display dialogs.</param>
    /// <param name="logger">Logger for diagnostic information.</param>
    public HistoryViewModel(
        IMountHistoryService historyService,
        IDialogService dialogService,
        ILogger<HistoryViewModel> logger)
    {
        _historyService = historyService;
        _dialogService = dialogService;
        _logger = logger;
    }

    /// <summary>
    /// Loads mount history from the history service.
    /// </summary>
    public async Task LoadHistoryAsync()
    {
        try
        {
            StatusMessage = "Loading history...";
            _logger.LogInformation("Loading mount history");

            var history = await _historyService.GetHistoryAsync();

            HistoryEntries.Clear();
            foreach (var entry in history.OrderByDescending(e => e.Timestamp))
            {
                HistoryEntries.Add(new MountHistoryEntryDisplay(entry));
            }

            StatusMessage = $"{HistoryEntries.Count} history {(HistoryEntries.Count == 1 ? "entry" : "entries")} loaded.";
            _logger.LogInformation("Loaded {Count} history entries", HistoryEntries.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load mount history");
            StatusMessage = $"Error loading history: {ex.Message}";
            await _dialogService.ShowErrorAsync($"Failed to load mount history:\n\n{ex.Message}", "Error");
        }
    }

    /// <summary>
    /// Refreshes the history display by reloading from the service.
    /// </summary>
    [RelayCommand]
    private async Task RefreshAsync()
    {
        await LoadHistoryAsync();
    }

    /// <summary>
    /// Clears all mount history after user confirmation.
    /// </summary>
    [RelayCommand]
    private async Task ClearHistoryAsync()
    {
        var confirmed = await _dialogService.ConfirmAsync(
            "Are you sure you want to clear all mount history?\n\nThis action cannot be undone.",
            "Clear History",
            DialogType.Warning);

        if (!confirmed)
        {
            return;
        }

        try
        {
            _logger.LogInformation("Clearing mount history");
            await _historyService.ClearHistoryAsync();

            HistoryEntries.Clear();
            StatusMessage = "History cleared.";

            await _dialogService.ShowInfoAsync("Mount history has been cleared.", "Success");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clear mount history");
            StatusMessage = "Failed to clear history.";
            await _dialogService.ShowErrorAsync($"Failed to clear history:\n\n{ex.Message}", "Error");
        }
    }
}

/// <summary>
/// Display model for a mount history entry with formatted properties for UI binding.
/// </summary>
public class MountHistoryEntryDisplay
{
    private readonly MountHistoryEntry _entry;

    public MountHistoryEntryDisplay(MountHistoryEntry entry)
    {
        _entry = entry;
    }

    public DateTime Timestamp => _entry.Timestamp;
    public string OperationType => _entry.OperationType;
    public bool Success => _entry.Success;
    public string StatusDisplay => Success ? "Success" : "Failed";
    public int DiskIndex => _entry.DiskIndex;
    public int? PartitionNumber => _entry.PartitionNumber;
    public char? DriveLetter => _entry.DriveLetter;
    public string DriveLetterDisplay => DriveLetter.HasValue ? $"{DriveLetter}:" : "-";
    public string? DistroName => _entry.DistroName;

    public string DetailsDisplay
    {
        get
        {
            if (!Success && !string.IsNullOrEmpty(_entry.ErrorMessage))
            {
                var failedStep = !string.IsNullOrEmpty(_entry.FailedStep) ? $" during {_entry.FailedStep}" : "";
                return $"Error{failedStep}: {_entry.ErrorMessage}";
            }

            if (OperationType == "Mount" && !string.IsNullOrEmpty(_entry.MountPathUNC))
            {
                return $"UNC: {_entry.MountPathUNC}";
            }

            if (Success)
            {
                return OperationType == "Unmount" ? "Drive unmounted successfully" : "Completed successfully";
            }

            return "Operation failed";
        }
    }
}
