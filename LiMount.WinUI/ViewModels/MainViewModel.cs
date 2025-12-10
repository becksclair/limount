using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using LiMount.Core.Abstractions;
using LiMount.Core.Interfaces;
using LiMount.Core.Configuration;
using LiMount.Core.ViewModels;
using LiMount.WinUI.Views;

namespace LiMount.WinUI.ViewModels;

/// <summary>
/// WinUI-specific MainViewModel implementation.
/// Inherits all shared logic from BaseMainViewModel and provides platform-specific overrides.
/// </summary>
public partial class MainViewModel : BaseMainViewModel
{
    private readonly IUiDispatcher _uiDispatcher;
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
        IOptions<LiMountConfiguration> config,
        IUiDispatcher uiDispatcher)
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
        _uiDispatcher = uiDispatcher ?? throw new ArgumentNullException(nameof(uiDispatcher));
    }

    /// <inheritdoc/>
    protected override Task RunOnUiThreadAsync(Func<Task> action)
    {
        return _uiDispatcher.RunAsync(action);
    }

    /// <inheritdoc/>
    protected override void OpenExplorerCore(char driveLetter)
    {
        var drivePath = $"{driveLetter}:\\";
        Process.Start(new ProcessStartInfo("explorer.exe", drivePath) { UseShellExecute = true });
    }

    /// <inheritdoc/>
    protected override void OpenHistoryWindowCore()
    {
        var historyWindow = _historyWindowFactory();
        historyWindow.Activate();
    }
}
