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
public partial class MainViewModel(
    MainViewModel.MountingServices mountingServices,
    MainViewModel.AppServices appServices,
    Func<HistoryWindow> historyWindowFactory,
    IUiDispatcher uiDispatcher)
    : BaseMainViewModel(CreateDependencies(mountingServices, appServices))
{
    private readonly IUiDispatcher _uiDispatcher = uiDispatcher ?? throw new ArgumentNullException(nameof(uiDispatcher));
    private readonly Func<HistoryWindow> _historyWindowFactory = historyWindowFactory ?? throw new ArgumentNullException(nameof(historyWindowFactory));

    public sealed record MountingServices(
        IDiskEnumerationService DiskService,
        IDriveLetterService DriveLetterService,
        IMountOrchestrator MountOrchestrator,
        IUnmountOrchestrator UnmountOrchestrator,
        IMountStateService MountStateService,
        IFilesystemDetectionService FilesystemDetectionService);

    public sealed record AppServices(
        IEnvironmentValidationService EnvironmentValidationService,
        IDialogService DialogService,
        ILogger<MainViewModel> Logger,
        IOptions<LiMountConfiguration> Config);

    private static Dependencies CreateDependencies(MainViewModel.MountingServices mountingServices, MainViewModel.AppServices appServices)
    {
        var safeMountingServices = mountingServices ?? throw new ArgumentNullException(nameof(mountingServices));
        var safeAppServices = appServices ?? throw new ArgumentNullException(nameof(appServices));

        return new Dependencies(
            safeMountingServices.DiskService,
            safeMountingServices.DriveLetterService,
            safeMountingServices.MountOrchestrator,
            safeMountingServices.UnmountOrchestrator,
            safeMountingServices.MountStateService,
            safeAppServices.EnvironmentValidationService,
            safeMountingServices.FilesystemDetectionService,
            safeAppServices.DialogService,
            safeAppServices.Logger,
            safeAppServices.Config.Value);
    }

    /// <inheritdoc/>
    protected override Task RunOnUiThreadAsync(Func<Task> action)
    {
        return _uiDispatcher.RunAsync(action);
    }

    /// <inheritdoc/>
    protected override void OpenExplorerCore(string path)
    {
        Process.Start(new ProcessStartInfo("explorer.exe", path) { UseShellExecute = true });
    }

    /// <inheritdoc/>
    protected override void OpenHistoryWindowCore()
    {
        var historyWindow = _historyWindowFactory();
        historyWindow.Activate();
    }
}
