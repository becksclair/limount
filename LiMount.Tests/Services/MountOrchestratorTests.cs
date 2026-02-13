using FluentAssertions;
using LiMount.Core.Configuration;
using LiMount.Core.Interfaces;
using LiMount.Core.Models;
using LiMount.Core.Results;
using LiMount.Core.Services;
using Microsoft.Extensions.Options;
using Moq;

namespace LiMount.Tests.Services;

public class MountOrchestratorTests
{
    private readonly Mock<IMountScriptService> _mountScriptService = new();
    private readonly Mock<IWindowsAccessService> _windowsAccessService = new();
    private readonly Mock<IMountHistoryService> _historyService = new();
    private readonly Mock<IMountStateService> _mountStateService = new();
    private readonly MountOrchestrator _orchestrator;
    private readonly string _existingUncPath;

    public MountOrchestratorTests()
    {
        _existingUncPath = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var config = Options.Create(new LiMountConfiguration
        {
            MountOperations = new MountOperationsConfig
            {
                UncAccessibilityRetries = 1,
                UncAccessibilityDelayMs = 20,
                UncExistenceCheckTimeoutMs = 1000
            }
        });

        _mountScriptService
            .Setup(s => s.ExecuteUnmountScriptAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UnmountResult { Success = true });

        _windowsAccessService
            .Setup(s => s.CreateAccessAsync(It.IsAny<WindowsAccessRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<WindowsAccessInfo>.Success(new WindowsAccessInfo
            {
                AccessMode = WindowsAccessMode.DriveLetterLegacy,
                DriveLetter = 'Z',
                AccessPathUNC = _existingUncPath
            }));

        _orchestrator = new MountOrchestrator(
            _mountScriptService.Object,
            _windowsAccessService.Object,
            config,
            _historyService.Object,
            _mountStateService.Object);
    }

    [Fact]
    public async Task MountAndMapAsync_NegativeDiskIndex_ReturnsValidationError()
    {
        var result = await _orchestrator.MountAndMapAsync(-1, 1, WindowsAccessMode.DriveLetterLegacy, 'Z');
        result.Success.Should().BeFalse();
        result.FailedStep.Should().Be("validation");
    }

    [Fact]
    public async Task MountAndMapAsync_LegacyModeWithoutDriveLetter_ReturnsValidationError()
    {
        var result = await _orchestrator.MountAndMapAsync(1, 1, WindowsAccessMode.DriveLetterLegacy, null);
        result.Success.Should().BeFalse();
        result.FailedStep.Should().Be("validation");
    }

    [Fact]
    public async Task MountAndMapAsync_MountFailure_ReturnsMountFailure()
    {
        _mountScriptService
            .Setup(s => s.ExecuteMountScriptAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MountResult { Success = false, ErrorMessage = "WSL mount failed" });

        var result = await _orchestrator.MountAndMapAsync(1, 1, WindowsAccessMode.DriveLetterLegacy, 'Z');

        result.Success.Should().BeFalse();
        result.FailedStep.Should().Be("mount");
        result.ErrorMessage.Should().Contain("WSL mount failed");
    }

    [Fact]
    public async Task MountAndMapAsync_WindowsAccessFailure_RollsBackAndFailsMapStep()
    {
        _mountScriptService
            .Setup(s => s.ExecuteMountScriptAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MountResult
            {
                Success = true,
                DistroName = "Ubuntu",
                MountPathLinux = "/mnt/wsl/PHYSICALDRIVE1p1",
                MountPathUNC = _existingUncPath
            });

        _windowsAccessService
            .Setup(s => s.CreateAccessAsync(It.IsAny<WindowsAccessRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<WindowsAccessInfo>.Failure("Network location create failed", "map"));

        var result = await _orchestrator.MountAndMapAsync(1, 1, WindowsAccessMode.NetworkLocation);

        result.Success.Should().BeFalse();
        result.FailedStep.Should().Be("map");
        _mountScriptService.Verify(s => s.ExecuteUnmountScriptAsync(1, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task MountAndMapAsync_Success_LegacyMode_ReturnsDriveLetterAndPersistsState()
    {
        _mountScriptService
            .Setup(s => s.ExecuteMountScriptAsync(1, 1, "ext4", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MountResult
            {
                Success = true,
                DistroName = "Ubuntu",
                MountPathLinux = "/mnt/wsl/PHYSICALDRIVE1p1",
                MountPathUNC = _existingUncPath
            });

        _windowsAccessService
            .Setup(s => s.CreateAccessAsync(It.IsAny<WindowsAccessRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<WindowsAccessInfo>.Success(new WindowsAccessInfo
            {
                AccessMode = WindowsAccessMode.DriveLetterLegacy,
                DriveLetter = 'Z',
                AccessPathUNC = _existingUncPath
            }));

        var result = await _orchestrator.MountAndMapAsync(1, 1, WindowsAccessMode.DriveLetterLegacy, 'Z');

        result.Success.Should().BeTrue();
        result.AccessMode.Should().Be(WindowsAccessMode.DriveLetterLegacy);
        result.DriveLetter.Should().Be('Z');

        _mountStateService.Verify(
            s => s.RegisterMountAsync(
                It.Is<ActiveMount>(m =>
                    m.DiskIndex == 1 &&
                    m.PartitionNumber == 1 &&
                    m.AccessMode == WindowsAccessMode.DriveLetterLegacy &&
                    m.DriveLetter == 'Z'),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task MountAndMapAsync_Success_NoneMode_DoesNotRequireDriveLetter()
    {
        _mountScriptService
            .Setup(s => s.ExecuteMountScriptAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MountResult
            {
                Success = true,
                DistroName = "Ubuntu",
                MountPathLinux = "/mnt/wsl/PHYSICALDRIVE1p1",
                MountPathUNC = _existingUncPath
            });

        _windowsAccessService
            .Setup(s => s.CreateAccessAsync(It.IsAny<WindowsAccessRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<WindowsAccessInfo>.Success(new WindowsAccessInfo
            {
                AccessMode = WindowsAccessMode.None,
                AccessPathUNC = _existingUncPath
            }));

        var result = await _orchestrator.MountAndMapAsync(1, 1, WindowsAccessMode.None);

        result.Success.Should().BeTrue();
        result.AccessMode.Should().Be(WindowsAccessMode.None);
        result.DriveLetter.Should().BeNull();
    }
}

