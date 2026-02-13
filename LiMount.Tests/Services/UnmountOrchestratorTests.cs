using FluentAssertions;
using LiMount.Core.Interfaces;
using LiMount.Core.Models;
using LiMount.Core.Results;
using LiMount.Core.Services;
using Moq;

namespace LiMount.Tests.Services;

public class UnmountOrchestratorTests
{
    private readonly Mock<IMountScriptService> _mountScriptService = new();
    private readonly Mock<IWindowsAccessService> _windowsAccessService = new();
    private readonly Mock<IMountHistoryService> _historyService = new();
    private readonly UnmountOrchestrator _orchestrator;

    public UnmountOrchestratorTests()
    {
        _orchestrator = new UnmountOrchestrator(
            _mountScriptService.Object,
            _windowsAccessService.Object,
            _historyService.Object);
    }

    [Fact]
    public async Task UnmountAndUnmapAsync_NegativeDiskIndex_ReturnsValidationError()
    {
        var result = await _orchestrator.UnmountAndUnmapAsync(-1, WindowsAccessMode.DriveLetterLegacy, 'Z');
        result.Success.Should().BeFalse();
        result.FailedStep.Should().Be("validation");
    }

    [Fact]
    public async Task UnmountAndUnmapAsync_LegacyModeWithoutDriveLetter_ReturnsValidationError()
    {
        var result = await _orchestrator.UnmountAndUnmapAsync(1, WindowsAccessMode.DriveLetterLegacy, null);

        result.Success.Should().BeFalse();
        result.FailedStep.Should().Be("validation");
        result.AccessMode.Should().Be(WindowsAccessMode.DriveLetterLegacy);
        _windowsAccessService.Verify(
            s => s.RemoveAccessAsync(It.IsAny<WindowsAccessInfo>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _mountScriptService.Verify(
            s => s.ExecuteUnmountScriptAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task UnmountAndUnmapAsync_WindowsAccessFailure_StillUnmountsButReturnsFailure()
    {
        _windowsAccessService
            .Setup(s => s.RemoveAccessAsync(It.IsAny<WindowsAccessInfo>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure("Drive unmapping failed", "unmap"));

        _mountScriptService
            .Setup(s => s.ExecuteUnmountScriptAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UnmountResult { Success = true });

        var result = await _orchestrator.UnmountAndUnmapAsync(1, WindowsAccessMode.DriveLetterLegacy, 'Z');

        result.Success.Should().BeFalse();
        result.FailedStep.Should().Be("unmap");
        _mountScriptService.Verify(s => s.ExecuteUnmountScriptAsync(1, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UnmountAndUnmapAsync_UnmountFails_ReturnsUnmountFailure()
    {
        _windowsAccessService
            .Setup(s => s.RemoveAccessAsync(It.IsAny<WindowsAccessInfo>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        _mountScriptService
            .Setup(s => s.ExecuteUnmountScriptAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UnmountResult { Success = false, ErrorMessage = "Unmount failed" });

        var result = await _orchestrator.UnmountAndUnmapAsync(1, WindowsAccessMode.DriveLetterLegacy, 'Z');

        result.Success.Should().BeFalse();
        result.FailedStep.Should().Be("unmount");
    }

    [Fact]
    public async Task UnmountAndUnmapAsync_AlreadyDetachedUnmount_TreatedAsSuccess()
    {
        _windowsAccessService
            .Setup(s => s.RemoveAccessAsync(It.IsAny<WindowsAccessInfo>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        _mountScriptService
            .Setup(s => s.ExecuteUnmountScriptAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UnmountResult
            {
                Success = false,
                ErrorMessage = "wsl --unmount failed: Wsl/Service/DetachDisk/ERROR_FILE_NOT_FOUND"
            });

        var result = await _orchestrator.UnmountAndUnmapAsync(1, WindowsAccessMode.DriveLetterLegacy, 'Z');

        result.Success.Should().BeTrue();
        result.DriveLetter.Should().Be('Z');
    }

    [Fact]
    public async Task UnmountAndUnmapAsync_Success_ReturnsModeAndDriveLetter()
    {
        _windowsAccessService
            .Setup(s => s.RemoveAccessAsync(It.IsAny<WindowsAccessInfo>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        _mountScriptService
            .Setup(s => s.ExecuteUnmountScriptAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UnmountResult { Success = true });

        var result = await _orchestrator.UnmountAndUnmapAsync(1, WindowsAccessMode.DriveLetterLegacy, 'Z');

        result.Success.Should().BeTrue();
        result.AccessMode.Should().Be(WindowsAccessMode.DriveLetterLegacy);
        result.DriveLetter.Should().Be('Z');
    }
}
