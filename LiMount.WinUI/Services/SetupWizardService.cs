using LiMount.Core.Interfaces;
using LiMount.Core.Models;
using LiMount.WinUI.ViewModels;
using LiMount.WinUI.Views;
using Microsoft.UI.Xaml.Controls;

namespace LiMount.WinUI.Services;

/// <summary>
/// Coordinates first-run setup and settings persistence.
/// </summary>
public sealed class SetupWizardService : ISetupWizardService
{
    private readonly IUserSettingsService _userSettingsService;
    private readonly IPlatformCapabilityService _platformCapabilityService;
    private readonly IXamlRootProvider _xamlRootProvider;
    private readonly ILinkOpenerService _linkOpenerService;
    private readonly IHyperVEnableService _hyperVEnableService;
    private readonly Func<SetupWizardViewModel, CancellationToken, Task<ContentDialogResult>> _showDialogAsync;

    public SetupWizardService(
        IUserSettingsService userSettingsService,
        IPlatformCapabilityService platformCapabilityService,
        IXamlRootProvider xamlRootProvider,
        ILinkOpenerService linkOpenerService,
        IHyperVEnableService hyperVEnableService)
        : this(
            userSettingsService,
            platformCapabilityService,
            xamlRootProvider,
            linkOpenerService,
            hyperVEnableService,
            null)
    {
    }

    internal SetupWizardService(
        IUserSettingsService userSettingsService,
        IPlatformCapabilityService platformCapabilityService,
        IXamlRootProvider xamlRootProvider,
        ILinkOpenerService linkOpenerService,
        IHyperVEnableService hyperVEnableService,
        Func<SetupWizardViewModel, CancellationToken, Task<ContentDialogResult>>? showDialogAsync)
    {
        _userSettingsService = userSettingsService ?? throw new ArgumentNullException(nameof(userSettingsService));
        _platformCapabilityService = platformCapabilityService ?? throw new ArgumentNullException(nameof(platformCapabilityService));
        _xamlRootProvider = xamlRootProvider ?? throw new ArgumentNullException(nameof(xamlRootProvider));
        _linkOpenerService = linkOpenerService ?? throw new ArgumentNullException(nameof(linkOpenerService));
        _hyperVEnableService = hyperVEnableService ?? throw new ArgumentNullException(nameof(hyperVEnableService));
        _showDialogAsync = showDialogAsync ?? ShowDialogAsync;
    }

    public async Task<SetupWizardResult> EnsureSetupAsync(bool forceWizard = false, CancellationToken cancellationToken = default)
    {
        var settings = await _userSettingsService.LoadOrCreateAsync(cancellationToken);

        if (!forceWizard && settings.HasCompletedSetup)
        {
            return new SetupWizardResult
            {
                IsCompleted = true,
                Settings = settings
            };
        }

        var capabilities = await _platformCapabilityService.DetectAsync(cancellationToken);
        var viewModel = new SetupWizardViewModel(capabilities, settings);
        var result = await _showDialogAsync(viewModel, cancellationToken);
        if (result != ContentDialogResult.Primary)
        {
            return new SetupWizardResult
            {
                IsCompleted = false,
                WasCanceled = true,
                Settings = settings,
                Capabilities = capabilities
            };
        }

        viewModel.ApplyTo(settings);
        await _userSettingsService.SaveAsync(settings, cancellationToken);

        return new SetupWizardResult
        {
            IsCompleted = true,
            Settings = settings,
            Capabilities = capabilities
        };
    }

    private async Task<ContentDialogResult> ShowDialogAsync(SetupWizardViewModel viewModel, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var dialog = new SetupWizardDialog(viewModel, _linkOpenerService, _hyperVEnableService)
        {
            XamlRoot = await _xamlRootProvider.WaitForXamlRootAsync()
        };

        return await dialog.ShowAsync();
    }
}
