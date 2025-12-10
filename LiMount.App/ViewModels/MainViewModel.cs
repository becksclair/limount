using System.Diagnostics;
using System.Windows;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using LiMount.App.Services;
using LiMount.App.Views;
using LiMount.Core.Abstractions;
using LiMount.Core.Interfaces;
using LiMount.Core.Configuration;
using LiMount.Core.ViewModels;

namespace LiMount.App.ViewModels;

/// <summary>
/// WPF-specific MainViewModel implementation.
/// Inherits all shared logic from BaseMainViewModel and provides platform-specific overrides.
/// </summary>
public partial class MainViewModel : BaseMainViewModel
{
    private readonly Func<HistoryWindow> _historyWindowFactory;

    /// <summary>
    /// Initializes a new instance of MainViewModel with all required services.
    /// </summary>
    public MainViewModel(
        IDiskEnumerationService diskService,
        IDriveLetterService driveLetterService,
        IMountOrchestrator mountOrchestrator,
        IUnmountOrchestrator unmountOrchestrator,
        IMountStateService mountStateService,
        IEnvironmentValidationService environmentValidationService,
        IFilesystemDetectionService filesystemDetectionService,
        IDialogService dialogService,
        Func<HistoryWindow> historyWindowFactory,
        ILogger<MainViewModel> logger,
        IOptions<LiMountConfiguration> config)
        : base(
            diskService,
            driveLetterService,
            mountOrchestrator,
            unmountOrchestrator,
            mountStateService,
            environmentValidationService,
            filesystemDetectionService,
            dialogService,
            logger,
            config.Value)
    {
        _historyWindowFactory = historyWindowFactory ?? throw new ArgumentNullException(nameof(historyWindowFactory));
    }

    /// <inheritdoc/>
    protected override Task RunOnUiThreadAsync(Func<Task> action)
    {
        return Application.Current.Dispatcher.InvokeAsync(action).Task;
    }

    /// <inheritdoc/>
    protected override void OpenExplorerCore(char driveLetter)
    {
        var drivePath = $"{driveLetter}:\\";
        Process.Start("explorer.exe", drivePath);
    }

    /// <inheritdoc/>
    protected override void OpenHistoryWindowCore()
    {
        var historyWindow = _historyWindowFactory();
        historyWindow.Owner = Application.Current.MainWindow;
        historyWindow.ShowDialog();
    }
}
