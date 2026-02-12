using LiMount.Core.Interfaces;
using LiMount.Core.Models;
using LiMount.WinUI.Services;
using LiMount.WinUI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace LiMount.Tests.Services;

public sealed class SetupWizardServiceTests
{
    [Fact]
    public async Task EnsureSetupAsync_WhenAlreadyCompletedAndNotForced_SkipsDialog()
    {
        var settingsService = new FakeUserSettingsService(new UserSettings { HasCompletedSetup = true });
        var capabilityService = new FakeCapabilityService();
        var service = new SetupWizardService(
            settingsService,
            capabilityService,
            new NoopXamlRootProvider(),
            new NoopLinkOpener(),
            new NoopHyperVEnableService(),
            showDialogAsync: (_, _) =>
            {
                throw new InvalidOperationException("Dialog should not be shown.");
            });

        var result = await service.EnsureSetupAsync(forceWizard: false);

        Assert.True(result.IsCompleted);
        Assert.False(result.WasCanceled);
        Assert.Equal(0, settingsService.SaveCallCount);
        Assert.Equal(0, capabilityService.DetectCallCount);
    }

    [Fact]
    public async Task EnsureSetupAsync_WhenForced_ShowsDialogAndPersists()
    {
        var settingsService = new FakeUserSettingsService(new UserSettings { HasCompletedSetup = true });
        var capabilityService = new FakeCapabilityService();
        var service = new SetupWizardService(
            settingsService,
            capabilityService,
            new NoopXamlRootProvider(),
            new NoopLinkOpener(),
            new NoopHyperVEnableService(),
            showDialogAsync: (viewModel, _) =>
            {
                viewModel.SelectedAccessMode = WindowsAccessMode.NetworkLocation;
                viewModel.SelectedBackendPreference = MountBackendPreference.VmPreferred;
                return Task.FromResult(ContentDialogResult.Primary);
            });

        var result = await service.EnsureSetupAsync(forceWizard: true);

        Assert.True(result.IsCompleted);
        Assert.Equal(1, settingsService.SaveCallCount);
        Assert.True(settingsService.Current.HasCompletedSetup);
        Assert.Equal(MountBackendPreference.VmPreferred, settingsService.Current.BackendPreference);
    }

    [Fact]
    public async Task EnsureSetupAsync_WhenCanceled_ReturnsCanceledAndDoesNotSave()
    {
        var settingsService = new FakeUserSettingsService(new UserSettings { HasCompletedSetup = false });
        var capabilityService = new FakeCapabilityService();
        var service = new SetupWizardService(
            settingsService,
            capabilityService,
            new NoopXamlRootProvider(),
            new NoopLinkOpener(),
            new NoopHyperVEnableService(),
            showDialogAsync: (_, _) => Task.FromResult(ContentDialogResult.None));

        var result = await service.EnsureSetupAsync(forceWizard: false);

        Assert.False(result.IsCompleted);
        Assert.True(result.WasCanceled);
        Assert.Equal(0, settingsService.SaveCallCount);
    }

    [Fact]
    public async Task EnsureSetupAsync_FirstRun_AppliesAutomaticRecommendedDefaults()
    {
        var settingsService = new FakeUserSettingsService(new UserSettings { HasCompletedSetup = false });
        var capabilityService = new ConfigurableCapabilityService(new PlatformCapabilities
        {
            WindowsEdition = "Professional",
            HyperVSupported = true,
            HyperVEnabled = true,
            HyperVCmdletsAvailable = true,
            WslInstalled = true,
            WslMountSupported = true,
            DefaultDistroPresent = true,
            VmwareInstalled = true,
            VirtualBoxInstalled = true
        });

        var service = new SetupWizardService(
            settingsService,
            capabilityService,
            new NoopXamlRootProvider(),
            new NoopLinkOpener(),
            new NoopHyperVEnableService(),
            showDialogAsync: (_, _) => Task.FromResult(ContentDialogResult.Primary));

        var result = await service.EnsureSetupAsync(forceWizard: false);

        Assert.True(result.IsCompleted);
        Assert.True(settingsService.Current.HasCompletedSetup);
        Assert.Equal(MountBackendPreference.WslPreferred, settingsService.Current.BackendPreference);
        Assert.Equal(VmFallbackPolicy.OnFsIncompatibility, settingsService.Current.VmFallbackPolicy);
        Assert.Equal(HypervisorSelection.Auto, settingsService.Current.Hypervisor);
        Assert.Equal(WindowsAccessMode.NetworkLocation, settingsService.Current.AccessMode);
    }

    private sealed class FakeUserSettingsService : IUserSettingsService
    {
        public FakeUserSettingsService(UserSettings initial)
        {
            Current = initial;
        }

        public UserSettings Current { get; private set; }

        public int SaveCallCount { get; private set; }

        public Task<UserSettings> LoadOrCreateAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(Current);

        public Task SaveAsync(UserSettings settings, CancellationToken cancellationToken = default)
        {
            SaveCallCount++;
            Current = settings;
            return Task.CompletedTask;
        }

        public Task ResetAsync(CancellationToken cancellationToken = default)
        {
            Current = new UserSettings();
            return Task.CompletedTask;
        }
    }

    private sealed class FakeCapabilityService : IPlatformCapabilityService
    {
        public int DetectCallCount { get; private set; }

        public Task<PlatformCapabilities> DetectAsync(CancellationToken cancellationToken = default)
        {
            DetectCallCount++;
            return Task.FromResult(new PlatformCapabilities
            {
                WindowsEdition = "Professional",
                HyperVSupported = true,
                HyperVEnabled = true,
                HyperVCmdletsAvailable = true,
                WslInstalled = true,
                WslMountSupported = true,
                DefaultDistroPresent = true
            });
        }
    }

    private sealed class ConfigurableCapabilityService : IPlatformCapabilityService
    {
        private readonly PlatformCapabilities _capabilities;

        public ConfigurableCapabilityService(PlatformCapabilities capabilities)
        {
            _capabilities = capabilities;
        }

        public Task<PlatformCapabilities> DetectAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(_capabilities);
    }

    private sealed class NoopXamlRootProvider : IXamlRootProvider
    {
        public XamlRoot? GetXamlRoot() => null;

        public void SetXamlRoot(XamlRoot root)
        {
        }

        public Task<XamlRoot> WaitForXamlRootAsync(int timeoutMs = 5000)
            => throw new InvalidOperationException("Not used in tests.");
    }

    private sealed class NoopLinkOpener : ILinkOpenerService
    {
        public bool TryOpen(string url, out string? errorMessage)
        {
            errorMessage = null;
            return true;
        }
    }

    private sealed class NoopHyperVEnableService : IHyperVEnableService
    {
        public Task<HyperVEnableResult> EnableAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(HyperVEnableResult.Completed(requiresRestart: true));
    }
}
