using LiMount.Core.Constants;
using LiMount.Core.Models;
using LiMount.WinUI.Services;
using LiMount.WinUI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Linq;

namespace LiMount.WinUI.Views;

/// <summary>
/// First-run setup wizard dialog.
/// </summary>
public sealed partial class SetupWizardDialog : ContentDialog
{
    private readonly ILinkOpenerService _linkOpenerService;
    private readonly IHyperVEnableService _hyperVEnableService;

    public SetupWizardViewModel ViewModel { get; }

    public SetupWizardDialog(
        SetupWizardViewModel viewModel,
        ILinkOpenerService linkOpenerService,
        IHyperVEnableService hyperVEnableService)
    {
        ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _linkOpenerService = linkOpenerService ?? throw new ArgumentNullException(nameof(linkOpenerService));
        _hyperVEnableService = hyperVEnableService ?? throw new ArgumentNullException(nameof(hyperVEnableService));

        InitializeComponent();
        DataContext = ViewModel;

        Loaded += SetupWizardDialog_Loaded;
    }

    private void SetupWizardDialog_Loaded(object sender, RoutedEventArgs e)
    {
        ApplyInitialSelections();
    }

    private void ApplyInitialSelections()
    {
        BackendPreferenceCombo.SelectedItem = ViewModel.Backends
            .FirstOrDefault(option => option.Value == ViewModel.SelectedBackendPreference);
        FallbackPolicyCombo.SelectedItem = ViewModel.FallbackPolicies
            .FirstOrDefault(option => option.Value == ViewModel.SelectedVmFallbackPolicy);
        AccessModeCombo.SelectedItem = ViewModel.AccessModes
            .FirstOrDefault(option => option.Value == ViewModel.SelectedAccessMode);
        SetInitialHypervisorSelection();
    }

    private void SetInitialHypervisorSelection()
    {
        switch (ViewModel.SelectedHypervisor)
        {
            case HypervisorSelection.HyperV:
                HyperVRadio.IsChecked = true;
                break;
            case HypervisorSelection.VMware:
                VmwareRadio.IsChecked = true;
                break;
            case HypervisorSelection.VirtualBox:
                VirtualBoxRadio.IsChecked = true;
                break;
            default:
                AutoHypervisorRadio.IsChecked = true;
                break;
        }
    }

    private async void EnableHyperV_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button)
        {
            return;
        }

        button.IsEnabled = false;
        ViewModel.HyperVActionStatus = "Running elevated Hyper-V enablement command. UAC consent is required.";

        var result = await _hyperVEnableService.EnableAsync();
        if (result.Success)
        {
            ViewModel.HyperVActionStatus = result.RequiresRestart
                ? "Hyper-V enablement command completed. A restart is required before Hyper-V is fully available."
                : "Hyper-V enablement command completed successfully.";
        }
        else if (result.WasCanceledByUser)
        {
            ViewModel.HyperVActionStatus = "Hyper-V enablement was canceled at the UAC prompt.";
        }
        else
        {
            ViewModel.HyperVActionStatus = $"Hyper-V enablement failed: {result.ErrorMessage}";
        }

        button.IsEnabled = true;
    }

    private void OpenHyperVDocs_Click(object sender, RoutedEventArgs e)
    {
        OpenLink(ExternalLinks.HyperVInstallDocs);
    }

    private void OpenWslDocs_Click(object sender, RoutedEventArgs e)
    {
        OpenLink(ExternalLinks.WslInstallDocs);
    }

    private void OpenVmwareDownload_Click(object sender, RoutedEventArgs e)
    {
        OpenLink(ExternalLinks.VmwareWorkstationDownload);
    }

    private void OpenVirtualBoxDownload_Click(object sender, RoutedEventArgs e)
    {
        OpenLink(ExternalLinks.VirtualBoxDownloads);
    }

    private void OpenHyperVCompatHelp_Click(object sender, RoutedEventArgs e)
    {
        OpenLink(ExternalLinks.HyperVVirtualizationAppsTroubleshooting);
    }

    private void TestConfiguration_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.HyperVActionStatus = "Configuration checks complete. Save and Continue to persist these settings.";
    }

    private void AutoHypervisorRadio_Checked(object sender, RoutedEventArgs e)
    {
        ViewModel.SelectedHypervisor = HypervisorSelection.Auto;
    }

    private void HyperVRadio_Checked(object sender, RoutedEventArgs e)
    {
        ViewModel.SelectedHypervisor = HypervisorSelection.HyperV;
    }

    private void VmwareRadio_Checked(object sender, RoutedEventArgs e)
    {
        ViewModel.SelectedHypervisor = HypervisorSelection.VMware;
    }

    private void VirtualBoxRadio_Checked(object sender, RoutedEventArgs e)
    {
        ViewModel.SelectedHypervisor = HypervisorSelection.VirtualBox;
    }

    private void OpenLink(string url)
    {
        if (_linkOpenerService.TryOpen(url, out var error))
        {
            return;
        }

        ViewModel.HyperVActionStatus = $"Failed to open link: {error}";
    }
}
