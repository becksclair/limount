using System.Text;
using FluentAssertions;
using LiMount.Core.Models;
using LiMount.Core.Services;

namespace LiMount.Tests.Services;

public sealed class UserSettingsServiceTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), $"limount-usersettings-{Guid.NewGuid():N}");
    private readonly string _settingsPath;

    public UserSettingsServiceTests()
    {
        Directory.CreateDirectory(_tempDirectory);
        _settingsPath = Path.Combine(_tempDirectory, "settings.json");
    }

    [Fact]
    public async Task LoadOrCreateAsync_WhenMissing_ReturnsDefaults()
    {
        using var service = new UserSettingsService(settingsFilePath: _settingsPath);

        var settings = await service.LoadOrCreateAsync();

        settings.HasCompletedSetup.Should().BeFalse();
        settings.BackendPreference.Should().Be(MountBackendPreference.WslPreferred);
        settings.VmFallbackPolicy.Should().Be(VmFallbackPolicy.Disabled);
        settings.Hypervisor.Should().Be(HypervisorSelection.Auto);
        settings.AccessMode.Should().Be(WindowsAccessMode.NetworkLocation);
        settings.Version.Should().Be(1);
    }

    [Fact]
    public async Task SaveAsync_RoundTripsSettings()
    {
        using var service = new UserSettingsService(settingsFilePath: _settingsPath);
        var input = new UserSettings
        {
            Version = 3,
            HasCompletedSetup = true,
            BackendPreference = MountBackendPreference.VmPreferred,
            VmFallbackPolicy = VmFallbackPolicy.OnFsIncompatibility,
            Hypervisor = HypervisorSelection.HyperV,
            AccessMode = WindowsAccessMode.DriveLetterLegacy,
            VmAppliance = new VmApplianceSettings
            {
                VmName = "CustomAppliance",
                StoragePath = @"D:\LiMount",
                UseExistingVm = true
            },
            GuestAuth = new GuestAuthSettings
            {
                Host = "192.168.0.10",
                Username = "limount",
                UseSshKey = false
            }
        };

        await service.SaveAsync(input);
        var loaded = await service.LoadOrCreateAsync();

        loaded.Version.Should().Be(3);
        loaded.HasCompletedSetup.Should().BeTrue();
        loaded.BackendPreference.Should().Be(MountBackendPreference.VmPreferred);
        loaded.VmFallbackPolicy.Should().Be(VmFallbackPolicy.OnFsIncompatibility);
        loaded.Hypervisor.Should().Be(HypervisorSelection.HyperV);
        loaded.AccessMode.Should().Be(WindowsAccessMode.DriveLetterLegacy);
        loaded.VmAppliance.VmName.Should().Be("CustomAppliance");
        loaded.GuestAuth.Username.Should().Be("limount");
    }

    [Fact]
    public async Task LoadOrCreateAsync_WhenCorrupt_ReturnsDefaults()
    {
        await File.WriteAllTextAsync(_settingsPath, "{this-is-invalid-json}", Encoding.UTF8);
        using var service = new UserSettingsService(settingsFilePath: _settingsPath);

        var loaded = await service.LoadOrCreateAsync();

        loaded.Version.Should().Be(1);
        loaded.HasCompletedSetup.Should().BeFalse();
        loaded.AccessMode.Should().Be(WindowsAccessMode.NetworkLocation);
    }

    [Fact]
    public async Task LoadOrCreateAsync_WhenOldVersion_NormalizesToVersion1()
    {
        const string json = """
        {
          "Version": 0,
          "HasCompletedSetup": true,
          "BackendPreference": 1
        }
        """;

        await File.WriteAllTextAsync(_settingsPath, json, Encoding.UTF8);
        using var service = new UserSettingsService(settingsFilePath: _settingsPath);

        var loaded = await service.LoadOrCreateAsync();

        loaded.Version.Should().Be(1);
        loaded.HasCompletedSetup.Should().BeTrue();
        loaded.VmAppliance.Should().NotBeNull();
        loaded.GuestAuth.Should().NotBeNull();
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, recursive: true);
            }
        }
        catch
        {
            // best effort
        }
    }
}
