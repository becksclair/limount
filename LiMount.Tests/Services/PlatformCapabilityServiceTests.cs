using FluentAssertions;
using LiMount.Core.Models;
using LiMount.Core.Services;

namespace LiMount.Tests.Services;

public sealed class PlatformCapabilityServiceTests
{
    [Fact]
    public async Task DetectAsync_MapsCapabilitiesFromCommandOutputs()
    {
        var responses = new Dictionary<string, PlatformCommandResult>(StringComparer.OrdinalIgnoreCase)
        {
            ["dism.exe|/Online /Get-FeatureInfo /FeatureName:Microsoft-Hyper-V-All"] =
                new PlatformCommandResult(0, "State : Enabled", string.Empty),
            ["wsl.exe|--status"] = new PlatformCommandResult(0, "Default Distribution: Ubuntu", string.Empty),
            ["wsl.exe|--help"] = new PlatformCommandResult(0, "Usage: wsl.exe --mount <Disk>", string.Empty),
            ["wsl.exe|--list --quiet"] = new PlatformCommandResult(0, "Ubuntu\n", string.Empty),
            ["where.exe|vmrun.exe"] = new PlatformCommandResult(0, @"C:\Program Files\VMware\vmrun.exe", string.Empty),
            ["where.exe|VBoxManage.exe"] = new PlatformCommandResult(1, string.Empty, "INFO: Could not find files")
        };

        Task<PlatformCommandResult> Runner(string file, string args, System.Text.Encoding? encoding, CancellationToken cancellationToken)
        {
            var key = $"{file}|{args}";
            return Task.FromResult(responses.TryGetValue(key, out var result)
                ? result
                : new PlatformCommandResult(1, string.Empty, "not mapped"));
        }

        var service = new PlatformCapabilityService(
            logger: null,
            commandRunner: Runner,
            windowsEditionProvider: () => "Professional",
            memoryProvider: () => 8L * 1024 * 1024 * 1024,
            fileExists: _ => false,
            registryValueProvider: (_, _, _, _) => null,
            serviceExists: serviceName => serviceName.Equals("vmms", StringComparison.OrdinalIgnoreCase),
            hypervisorPresentProvider: () => false);

        var capabilities = await service.DetectAsync();

        capabilities.WindowsEdition.Should().Be("Professional");
        capabilities.HyperVSupported.Should().BeTrue();
        capabilities.HyperVEnabled.Should().BeTrue();
        capabilities.HyperVCmdletsAvailable.Should().BeTrue();
        capabilities.WslInstalled.Should().BeTrue();
        capabilities.WslMountSupported.Should().BeTrue();
        capabilities.DefaultDistroPresent.Should().BeTrue();
        capabilities.VmwareInstalled.Should().BeTrue();
        capabilities.VirtualBoxInstalled.Should().BeFalse();
        capabilities.HostMemoryBytes.Should().Be(8L * 1024 * 1024 * 1024);
        capabilities.HyperVUnavailableReason.Should().BeNull();
        capabilities.VirtualBoxUnavailableReason.Should().Contain("not found");
    }

    [Fact]
    public async Task DetectAsync_HyperVEnabledFallback_WorksWithoutElevation()
    {
        var responses = new Dictionary<string, PlatformCommandResult>(StringComparer.OrdinalIgnoreCase)
        {
            ["dism.exe|/Online /Get-FeatureInfo /FeatureName:Microsoft-Hyper-V-All"] =
                new PlatformCommandResult(1, string.Empty, "The requested operation requires elevation."),
            ["wsl.exe|--status"] = new PlatformCommandResult(0, "Default Distribution: Ubuntu", string.Empty),
            ["wsl.exe|--help"] = new PlatformCommandResult(0, "Usage: wsl.exe --mount <Disk>", string.Empty),
            ["wsl.exe|--list --quiet"] = new PlatformCommandResult(0, "Ubuntu\n", string.Empty),
            ["where.exe|vmrun.exe"] = new PlatformCommandResult(1, string.Empty, "not found"),
            ["where.exe|VBoxManage.exe"] = new PlatformCommandResult(1, string.Empty, "not found")
        };

        Task<PlatformCommandResult> Runner(string file, string args, System.Text.Encoding? encoding, CancellationToken cancellationToken)
        {
            var key = $"{file}|{args}";
            return Task.FromResult(responses.TryGetValue(key, out var result)
                ? result
                : new PlatformCommandResult(1, string.Empty, "not mapped"));
        }

        var service = new PlatformCapabilityService(
            logger: null,
            commandRunner: Runner,
            windowsEditionProvider: () => "Professional",
            memoryProvider: () => 8L * 1024 * 1024 * 1024,
            fileExists: _ => false,
            registryValueProvider: (_, _, _, _) => null,
            serviceExists: serviceName => serviceName.Equals("vmms", StringComparison.OrdinalIgnoreCase),
            hypervisorPresentProvider: () => true);

        var capabilities = await service.DetectAsync();

        capabilities.HyperVEnabled.Should().BeTrue();
        capabilities.HyperVCmdletsAvailable.Should().BeTrue();
        capabilities.HyperVUnavailableReason.Should().BeNull();
    }

    [Fact]
    public async Task DetectAsync_WhenCommandsFail_ProducesUnavailableReasons()
    {
        Task<PlatformCommandResult> Runner(string _, string __, System.Text.Encoding? ___, CancellationToken ____)
            => Task.FromResult(new PlatformCommandResult(1, string.Empty, "failed"));

        var service = new PlatformCapabilityService(
            logger: null,
            commandRunner: Runner,
            windowsEditionProvider: () => "Home",
            memoryProvider: () => 4L * 1024 * 1024 * 1024,
            fileExists: _ => false,
            registryValueProvider: (_, _, _, _) => null,
            serviceExists: _ => false,
            hypervisorPresentProvider: () => false);

        var capabilities = await service.DetectAsync();

        capabilities.HyperVSupported.Should().BeFalse();
        capabilities.WslInstalled.Should().BeFalse();
        capabilities.VmwareInstalled.Should().BeFalse();
        capabilities.VirtualBoxInstalled.Should().BeFalse();
        capabilities.HyperVUnavailableReason.Should().Contain("not supported");
        capabilities.WslUnavailableReason.Should().Contain("not installed");
    }

    [Theory]
    [InlineData("Home", false)]
    [InlineData("Professional", true)]
    [InlineData("Enterprise", true)]
    [InlineData("Education", true)]
    [InlineData("ServerStandard", true)]
    public void IsHyperVEditionSupported_ReturnsExpectedValue(string edition, bool expected)
    {
        PlatformCapabilityService.IsHyperVEditionSupported(edition).Should().Be(expected);
    }

    [Fact]
    public void BuildReasonHelpers_ReturnExpectedMessages()
    {
        PlatformCapabilityService.BuildHyperVUnavailableReason(new PlatformCapabilities
        {
            HyperVSupported = false
        }).Should().Contain("not supported");

        PlatformCapabilityService.BuildWslUnavailableReason(new PlatformCapabilities
        {
            WslInstalled = true,
            WslMountSupported = false
        }).Should().Contain("wsl --mount");
    }

    [Fact]
    public async Task DetectAsync_WhenWhereFails_ButProgramFilesPathExists_DetectsVmwareAndVirtualBox()
    {
        Task<PlatformCommandResult> Runner(string file, string args, System.Text.Encoding? _, CancellationToken __)
        {
            if (file.Equals("where.exe", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(new PlatformCommandResult(1, string.Empty, "not found"));
            }

            if (file.Equals("wsl.exe", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(args switch
                {
                    "--status" => new PlatformCommandResult(0, "ok", string.Empty),
                    "--help" => new PlatformCommandResult(0, "--mount", string.Empty),
                    "--list --quiet" => new PlatformCommandResult(0, "Ubuntu\n", string.Empty),
                    _ => new PlatformCommandResult(1, string.Empty, "unsupported")
                });
            }

            return Task.FromResult(new PlatformCommandResult(0, "Enabled", string.Empty));
        }

        var service = new PlatformCapabilityService(
            logger: null,
            commandRunner: Runner,
            windowsEditionProvider: () => "Professional",
            memoryProvider: () => 8L * 1024 * 1024 * 1024,
            fileExists: path =>
                path.EndsWith(@"VMware\VMware Workstation\vmrun.exe", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(@"Oracle\VirtualBox\VBoxManage.exe", StringComparison.OrdinalIgnoreCase),
            registryValueProvider: (_, _, _, _) => null,
            serviceExists: _ => false,
            hypervisorPresentProvider: () => false);

        var capabilities = await service.DetectAsync();
        capabilities.VmwareInstalled.Should().BeTrue();
        capabilities.VirtualBoxInstalled.Should().BeTrue();
    }

    [Fact]
    public async Task DetectAsync_WhenRegistryInstallPathPresent_DetectsVmware()
    {
        Task<PlatformCommandResult> Runner(string file, string args, System.Text.Encoding? _, CancellationToken __)
        {
            if (file.Equals("where.exe", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(new PlatformCommandResult(1, string.Empty, "not found"));
            }

            if (file.Equals("wsl.exe", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(args switch
                {
                    "--status" => new PlatformCommandResult(0, "ok", string.Empty),
                    "--help" => new PlatformCommandResult(0, "--mount", string.Empty),
                    "--list --quiet" => new PlatformCommandResult(0, "Ubuntu\n", string.Empty),
                    _ => new PlatformCommandResult(1, string.Empty, "unsupported")
                });
            }

            return Task.FromResult(new PlatformCommandResult(0, "Enabled", string.Empty));
        }

        var installPath = @"C:\Custom\VMware";
        var expectedVmrunPath = Path.Combine(installPath, "vmrun.exe");

        var service = new PlatformCapabilityService(
            logger: null,
            commandRunner: Runner,
            windowsEditionProvider: () => "Professional",
            memoryProvider: () => 8L * 1024 * 1024 * 1024,
            fileExists: path => string.Equals(path, expectedVmrunPath, StringComparison.OrdinalIgnoreCase),
            registryValueProvider: (_, _, subKeyPath, valueName) =>
                subKeyPath.Contains("VMware Workstation", StringComparison.OrdinalIgnoreCase) &&
                valueName.Equals("InstallPath", StringComparison.OrdinalIgnoreCase)
                    ? installPath
                    : null,
            serviceExists: _ => false,
            hypervisorPresentProvider: () => false);

        var capabilities = await service.DetectAsync();
        capabilities.VmwareInstalled.Should().BeTrue();
    }

    [Fact]
    public async Task DetectAsync_WhenWslHelpWritesMountToStdErr_DetectsWslMountSupport()
    {
        var responses = new Dictionary<string, PlatformCommandResult>(StringComparer.OrdinalIgnoreCase)
        {
            ["dism.exe|/Online /Get-FeatureInfo /FeatureName:Microsoft-Hyper-V-All"] =
                new PlatformCommandResult(0, "State : Enabled", string.Empty),
            ["wsl.exe|--status"] = new PlatformCommandResult(0, "Default Distribution: Ubuntu", string.Empty),
            ["wsl.exe|--help"] = new PlatformCommandResult(0, string.Empty, "Usage: wsl.exe --mount <Disk>"),
            ["wsl.exe|--list --quiet"] = new PlatformCommandResult(0, "Ubuntu\n", string.Empty),
            ["where.exe|vmrun.exe"] = new PlatformCommandResult(1, string.Empty, "not found"),
            ["where.exe|VBoxManage.exe"] = new PlatformCommandResult(1, string.Empty, "not found")
        };

        Task<PlatformCommandResult> Runner(string file, string args, System.Text.Encoding? encoding, CancellationToken cancellationToken)
        {
            var key = $"{file}|{args}";
            return Task.FromResult(responses.TryGetValue(key, out var result)
                ? result
                : new PlatformCommandResult(1, string.Empty, "not mapped"));
        }

        var service = new PlatformCapabilityService(
            logger: null,
            commandRunner: Runner,
            windowsEditionProvider: () => "Professional",
            memoryProvider: () => 8L * 1024 * 1024 * 1024,
            fileExists: _ => false,
            registryValueProvider: (_, _, _, _) => null,
            serviceExists: serviceName => serviceName.Equals("vmms", StringComparison.OrdinalIgnoreCase),
            hypervisorPresentProvider: () => false);

        var capabilities = await service.DetectAsync();

        capabilities.WslInstalled.Should().BeTrue();
        capabilities.WslMountSupported.Should().BeTrue();
        capabilities.WslUnavailableReason.Should().BeNull();
    }

    [Fact]
    public async Task DetectAsync_WhenWslHelpContainsNullInterleavedText_DetectsWslMountSupport()
    {
        static string NullInterleave(string value) => string.Concat(value.SelectMany(ch => new[] { ch, '\0' }));

        var responses = new Dictionary<string, PlatformCommandResult>(StringComparer.OrdinalIgnoreCase)
        {
            ["dism.exe|/Online /Get-FeatureInfo /FeatureName:Microsoft-Hyper-V-All"] =
                new PlatformCommandResult(0, "State : Enabled", string.Empty),
            ["wsl.exe|--status"] = new PlatformCommandResult(0, "Default Distribution: Ubuntu", string.Empty),
            ["wsl.exe|--help"] = new PlatformCommandResult(1, NullInterleave("Usage: wsl.exe --mount <Disk>"), string.Empty),
            ["wsl.exe|--list --quiet"] = new PlatformCommandResult(0, "Ubuntu\n", string.Empty),
            ["where.exe|vmrun.exe"] = new PlatformCommandResult(1, string.Empty, "not found"),
            ["where.exe|VBoxManage.exe"] = new PlatformCommandResult(1, string.Empty, "not found")
        };

        Task<PlatformCommandResult> Runner(string file, string args, System.Text.Encoding? encoding, CancellationToken cancellationToken)
        {
            var key = $"{file}|{args}";
            return Task.FromResult(responses.TryGetValue(key, out var result)
                ? result
                : new PlatformCommandResult(1, string.Empty, "not mapped"));
        }

        var service = new PlatformCapabilityService(
            logger: null,
            commandRunner: Runner,
            windowsEditionProvider: () => "Professional",
            memoryProvider: () => 8L * 1024 * 1024 * 1024,
            fileExists: _ => false,
            registryValueProvider: (_, _, _, _) => null,
            serviceExists: serviceName => serviceName.Equals("vmms", StringComparison.OrdinalIgnoreCase),
            hypervisorPresentProvider: () => false);

        var capabilities = await service.DetectAsync();

        capabilities.WslInstalled.Should().BeTrue();
        capabilities.WslMountSupported.Should().BeTrue();
        capabilities.WslUnavailableReason.Should().BeNull();
    }
}
