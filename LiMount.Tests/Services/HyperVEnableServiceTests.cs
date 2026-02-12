using System.ComponentModel;
using System.Diagnostics;
using FluentAssertions;
using LiMount.WinUI.Services;

namespace LiMount.Tests.Services;

public sealed class HyperVEnableServiceTests
{
    [Fact]
    public async Task EnableAsync_WhenProcessExitsZero_ReturnsSuccess()
    {
        var service = new HyperVEnableService(
            logger: null,
            processStarter: _ => Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/c exit 0",
                UseShellExecute = false,
                CreateNoWindow = true
            }));

        var result = await service.EnableAsync();

        result.Success.Should().BeTrue();
        result.RequiresRestart.Should().BeFalse();
        result.WasCanceledByUser.Should().BeFalse();
    }

    [Fact]
    public async Task EnableAsync_WhenProcessExitsNonZero_ReturnsFailure()
    {
        var service = new HyperVEnableService(
            logger: null,
            processStarter: _ => Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/c exit 5",
                UseShellExecute = false,
                CreateNoWindow = true
            }));

        var result = await service.EnableAsync();

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("code 5");
    }

    [Fact]
    public async Task EnableAsync_WhenUserCancelsUac_ReturnsCanceled()
    {
        var service = new HyperVEnableService(
            logger: null,
            processStarter: _ => throw new Win32Exception(1223));

        var result = await service.EnableAsync();

        result.Success.Should().BeFalse();
        result.WasCanceledByUser.Should().BeTrue();
    }

    [Fact]
    public async Task EnableAsync_UsesElevatedDismCommand()
    {
        ProcessStartInfo? capturedStartInfo = null;

        var service = new HyperVEnableService(
            logger: null,
            processStarter: startInfo =>
            {
                capturedStartInfo = startInfo;
                return Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/c exit 0",
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
            });

        var result = await service.EnableAsync();

        result.Success.Should().BeTrue();
        capturedStartInfo.Should().NotBeNull();
        capturedStartInfo!.FileName.Should().Be("dism.exe");
        capturedStartInfo.Arguments.Should().Contain("FeatureName:Microsoft-Hyper-V");
        capturedStartInfo.Verb.Should().Be("runas");
        capturedStartInfo.UseShellExecute.Should().BeTrue();
    }

    [Theory]
    [InlineData(0, true, false)]
    [InlineData(3010, true, true)]
    [InlineData(1, false, false)]
    public void FromExitCode_MapsExpectedOutcome(int exitCode, bool expectedSuccess, bool expectedRequiresRestart)
    {
        var result = HyperVEnableService.FromExitCode(exitCode);
        result.Success.Should().Be(expectedSuccess);
        result.RequiresRestart.Should().Be(expectedRequiresRestart);
    }
}
