using FluentAssertions;
using LiMount.Core.Models;
using LiMount.WinUI.ViewModels;

namespace LiMount.Tests.ViewModels;

public sealed class SetupWizardViewModelTests
{
    [Fact]
    public void FirstRun_WhenHyperVReady_AppliesRecommendedDefaults()
    {
        var capabilities = new PlatformCapabilities
        {
            HyperVSupported = true,
            HyperVEnabled = true,
            HyperVCmdletsAvailable = true,
            VmwareInstalled = true,
            VirtualBoxInstalled = true
        };

        var settings = new UserSettings
        {
            HasCompletedSetup = false
        };

        var vm = new SetupWizardViewModel(capabilities, settings);

        vm.SelectedBackendPreference.Should().Be(MountBackendPreference.WslPreferred);
        vm.SelectedVmFallbackPolicy.Should().Be(VmFallbackPolicy.OnFsIncompatibility);
        vm.SelectedHypervisor.Should().Be(HypervisorSelection.Auto);
        vm.SelectedAccessMode.Should().Be(WindowsAccessMode.NetworkLocation);

        vm.ShowEnableHyperVButton.Should().BeFalse();
        vm.ShowVmwareDownloadButton.Should().BeFalse();
        vm.ShowVirtualBoxDownloadButton.Should().BeFalse();
        vm.ShowHyperVActionStatus.Should().BeFalse();
        vm.HyperVActionStatus.Should().BeEmpty();
    }

    [Fact]
    public void FirstRun_WhenHyperVNotReadyAndVmwareInstalled_UsesVmwareFallbackDefaults()
    {
        var capabilities = new PlatformCapabilities
        {
            HyperVSupported = true,
            HyperVEnabled = false,
            HyperVCmdletsAvailable = false,
            VmwareInstalled = true,
            VirtualBoxInstalled = false
        };

        var vm = new SetupWizardViewModel(capabilities, new UserSettings { HasCompletedSetup = false });

        vm.SelectedBackendPreference.Should().Be(MountBackendPreference.WslPreferred);
        vm.SelectedVmFallbackPolicy.Should().Be(VmFallbackPolicy.OnFsIncompatibility);
        vm.SelectedHypervisor.Should().Be(HypervisorSelection.Auto);
        vm.SelectedAccessMode.Should().Be(WindowsAccessMode.NetworkLocation);

        vm.ShowEnableHyperVButton.Should().BeTrue();
        vm.ShowVmwareDownloadButton.Should().BeFalse();
        vm.ShowVirtualBoxDownloadButton.Should().BeTrue();
        vm.ShowHyperVActionStatus.Should().BeTrue();
        vm.HyperVActionStatus.Should().Contain("not fully ready");
    }

    [Fact]
    public void FirstRun_WhenNoVmProviderAvailable_DisablesFallbackAndKeepsAutoHypervisor()
    {
        var capabilities = new PlatformCapabilities
        {
            HyperVSupported = false,
            HyperVEnabled = false,
            HyperVCmdletsAvailable = false,
            VmwareInstalled = false,
            VirtualBoxInstalled = false
        };

        var vm = new SetupWizardViewModel(capabilities, new UserSettings { HasCompletedSetup = false });

        vm.SelectedBackendPreference.Should().Be(MountBackendPreference.WslPreferred);
        vm.SelectedVmFallbackPolicy.Should().Be(VmFallbackPolicy.Disabled);
        vm.SelectedHypervisor.Should().Be(HypervisorSelection.Auto);
        vm.SelectedAccessMode.Should().Be(WindowsAccessMode.NetworkLocation);

        vm.ShowEnableHyperVButton.Should().BeFalse();
        vm.ShowVmwareDownloadButton.Should().BeTrue();
        vm.ShowVirtualBoxDownloadButton.Should().BeTrue();
        vm.ShowHyperVActionStatus.Should().BeFalse();
    }

    [Fact]
    public void FirstRun_IgnoresUncommittedSettingsValuesAndUsesRecommendations()
    {
        var capabilities = new PlatformCapabilities
        {
            HyperVSupported = true,
            HyperVEnabled = true,
            HyperVCmdletsAvailable = true,
            VmwareInstalled = true,
            VirtualBoxInstalled = true
        };

        var settings = new UserSettings
        {
            HasCompletedSetup = false,
            BackendPreference = MountBackendPreference.VmOnly,
            VmFallbackPolicy = VmFallbackPolicy.Disabled,
            Hypervisor = HypervisorSelection.VirtualBox,
            AccessMode = WindowsAccessMode.DriveLetterLegacy
        };

        var vm = new SetupWizardViewModel(capabilities, settings);

        vm.SelectedBackendPreference.Should().Be(MountBackendPreference.WslPreferred);
        vm.SelectedVmFallbackPolicy.Should().Be(VmFallbackPolicy.OnFsIncompatibility);
        vm.SelectedHypervisor.Should().Be(HypervisorSelection.Auto);
        vm.SelectedAccessMode.Should().Be(WindowsAccessMode.NetworkLocation);
    }

    [Fact]
    public void ExistingSetup_PreservesUserSelections()
    {
        var capabilities = new PlatformCapabilities
        {
            HyperVSupported = true,
            HyperVEnabled = true,
            HyperVCmdletsAvailable = true,
            VmwareInstalled = true,
            VirtualBoxInstalled = true
        };

        var settings = new UserSettings
        {
            HasCompletedSetup = true,
            BackendPreference = MountBackendPreference.VmPreferred,
            VmFallbackPolicy = VmFallbackPolicy.OnSpecificErrors,
            Hypervisor = HypervisorSelection.VirtualBox,
            AccessMode = WindowsAccessMode.DriveLetterLegacy
        };

        var vm = new SetupWizardViewModel(capabilities, settings);

        vm.SelectedBackendPreference.Should().Be(MountBackendPreference.VmPreferred);
        vm.SelectedVmFallbackPolicy.Should().Be(VmFallbackPolicy.OnSpecificErrors);
        vm.SelectedHypervisor.Should().Be(HypervisorSelection.VirtualBox);
        vm.SelectedAccessMode.Should().Be(WindowsAccessMode.DriveLetterLegacy);
    }
}
