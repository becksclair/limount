using CommunityToolkit.Mvvm.ComponentModel;
using LiMount.Core.Models;

namespace LiMount.WinUI.ViewModels;

/// <summary>
/// ViewModel for first-run setup wizard.
/// </summary>
public sealed class SetupWizardViewModel : ObservableObject
{
    public SetupWizardViewModel(PlatformCapabilities capabilities, UserSettings currentSettings)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        ArgumentNullException.ThrowIfNull(currentSettings);

        Capabilities = capabilities;
        ApplyInitialSelections(currentSettings);

        Backends =
        [
            new BackendOption(MountBackendPreference.WslPreferred, "WSL first, VM fallback"),
            new BackendOption(MountBackendPreference.VmPreferred, "Always prefer VM"),
            new BackendOption(MountBackendPreference.VmOnly, "VM only")
        ];

        FallbackPolicies =
        [
            new FallbackPolicyOption(VmFallbackPolicy.Disabled, "Disabled"),
            new FallbackPolicyOption(VmFallbackPolicy.OnFsIncompatibility, "On filesystem incompatibility"),
            new FallbackPolicyOption(VmFallbackPolicy.OnSpecificErrors, "On specific errors")
        ];

        Hypervisors =
        [
            new HypervisorOption(HypervisorSelection.Auto, "Auto (recommended)"),
            new HypervisorOption(HypervisorSelection.HyperV, "Hyper-V"),
            new HypervisorOption(HypervisorSelection.VMware, "VMware Workstation"),
            new HypervisorOption(HypervisorSelection.VirtualBox, "VirtualBox")
        ];

        AccessModes =
        [
            new AccessModeOption(WindowsAccessMode.NetworkLocation, "Network Location (default)"),
            new AccessModeOption(WindowsAccessMode.DriveLetterLegacy, "Drive Letter (legacy)"),
            new AccessModeOption(WindowsAccessMode.None, "None")
        ];

        InitializeHyperVActionStatus();
        RefreshComputedValues();
    }

    public PlatformCapabilities Capabilities { get; }

    public IReadOnlyList<BackendOption> Backends { get; }

    public IReadOnlyList<FallbackPolicyOption> FallbackPolicies { get; }

    public IReadOnlyList<HypervisorOption> Hypervisors { get; }

    public IReadOnlyList<AccessModeOption> AccessModes { get; }

    private MountBackendPreference _selectedBackendPreference;
    private VmFallbackPolicy _selectedVmFallbackPolicy;
    private HypervisorSelection _selectedHypervisor;
    private WindowsAccessMode _selectedAccessMode;
    private string _hyperVActionStatus = string.Empty;
    private string _summaryText = string.Empty;

    public MountBackendPreference SelectedBackendPreference
    {
        get => _selectedBackendPreference;
        set
        {
            if (SetProperty(ref _selectedBackendPreference, value))
            {
                RefreshComputedValues();
            }
        }
    }

    public VmFallbackPolicy SelectedVmFallbackPolicy
    {
        get => _selectedVmFallbackPolicy;
        set
        {
            if (SetProperty(ref _selectedVmFallbackPolicy, value))
            {
                RefreshComputedValues();
            }
        }
    }

    public HypervisorSelection SelectedHypervisor
    {
        get => _selectedHypervisor;
        set
        {
            if (SetProperty(ref _selectedHypervisor, value))
            {
                RefreshComputedValues();
            }
        }
    }

    public WindowsAccessMode SelectedAccessMode
    {
        get => _selectedAccessMode;
        set
        {
            if (SetProperty(ref _selectedAccessMode, value))
            {
                RefreshComputedValues();
            }
        }
    }

    public string HyperVActionStatus
    {
        get => _hyperVActionStatus;
        set
        {
            if (SetProperty(ref _hyperVActionStatus, value))
            {
                OnPropertyChanged(nameof(ShowHyperVActionStatus));
            }
        }
    }

    public string SummaryText
    {
        get => _summaryText;
        private set => SetProperty(ref _summaryText, value);
    }

    public bool IsHyperVSelectable => Capabilities.HyperVSupported;

    public bool IsVmwareSelectable => Capabilities.VmwareInstalled;

    public bool IsVirtualBoxSelectable => Capabilities.VirtualBoxInstalled;

    public bool ShowEnableHyperVButton =>
        Capabilities.HyperVSupported && !IsHyperVReady;

    public bool ShowVmwareDownloadButton => !Capabilities.VmwareInstalled;

    public bool ShowVirtualBoxDownloadButton => !Capabilities.VirtualBoxInstalled;

    public bool ShowHyperVActionStatus => !string.IsNullOrWhiteSpace(HyperVActionStatus);

    public bool IsVmFlowEnabled =>
        SelectedBackendPreference is MountBackendPreference.VmOnly or MountBackendPreference.VmPreferred ||
        SelectedVmFallbackPolicy != VmFallbackPolicy.Disabled;

    public string HyperVAvailabilityText =>
        Capabilities.HyperVUnavailableReason ?? "Hyper-V is available.";

    public string VmwareAvailabilityText =>
        Capabilities.VmwareUnavailableReason ?? "VMware tooling detected.";

    public string VirtualBoxAvailabilityText =>
        Capabilities.VirtualBoxUnavailableReason ?? "VirtualBox tooling detected.";

    public string WslAvailabilityText =>
        Capabilities.WslUnavailableReason ?? "WSL mount prerequisites are available.";

    public void ApplyTo(UserSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        settings.Version = 1;
        settings.HasCompletedSetup = true;
        settings.BackendPreference = SelectedBackendPreference;
        settings.VmFallbackPolicy = SelectedVmFallbackPolicy;
        settings.Hypervisor = SelectedHypervisor;
        settings.AccessMode = SelectedAccessMode;
        settings.VmAppliance ??= new VmApplianceSettings();
        settings.GuestAuth ??= new GuestAuthSettings();
    }

    private void RefreshComputedValues()
    {
        OnPropertyChanged(nameof(IsVmFlowEnabled));
        SummaryText =
            $"Backend: {SelectedBackendPreference}\n" +
            $"Fallback: {SelectedVmFallbackPolicy}\n" +
            $"Hypervisor: {SelectedHypervisor}\n" +
            $"Access Mode: {SelectedAccessMode}";
    }

    private bool IsHyperVReady =>
        Capabilities.HyperVSupported &&
        Capabilities.HyperVEnabled &&
        Capabilities.HyperVCmdletsAvailable;

    private bool HasAnyVmProviderAvailable =>
        IsHyperVReady || Capabilities.VmwareInstalled || Capabilities.VirtualBoxInstalled;

    private void ApplyInitialSelections(UserSettings currentSettings)
    {
        if (currentSettings.HasCompletedSetup)
        {
            SelectedBackendPreference = currentSettings.BackendPreference;
            SelectedVmFallbackPolicy = currentSettings.VmFallbackPolicy;
            SelectedHypervisor = currentSettings.Hypervisor;
            SelectedAccessMode = currentSettings.AccessMode;
            return;
        }

        // First-run recommendations: keep WSL primary, configure fallback when a VM provider is available.
        SelectedBackendPreference = MountBackendPreference.WslPreferred;
        SelectedVmFallbackPolicy = HasAnyVmProviderAvailable
            ? VmFallbackPolicy.OnFsIncompatibility
            : VmFallbackPolicy.Disabled;
        SelectedHypervisor = HypervisorSelection.Auto;
        SelectedAccessMode = WindowsAccessMode.NetworkLocation;
    }

    private void InitializeHyperVActionStatus()
    {
        if (ShowEnableHyperVButton)
        {
            HyperVActionStatus = "Hyper-V is not fully ready. If you want Hyper-V fallback, use 'Enable Hyper-V (Admin)' and reboot if prompted.";
            return;
        }

        HyperVActionStatus = string.Empty;
    }

    public sealed record BackendOption(MountBackendPreference Value, string Label);

    public sealed record FallbackPolicyOption(VmFallbackPolicy Value, string Label);

    public sealed record HypervisorOption(HypervisorSelection Value, string Label);

    public sealed record AccessModeOption(WindowsAccessMode Value, string Label);
}
