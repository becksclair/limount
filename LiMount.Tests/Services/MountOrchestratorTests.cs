using Moq;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using LiMount.Core.Configuration;
using LiMount.Core.Interfaces;
using LiMount.Core.Models;
using LiMount.Core.Services;

namespace LiMount.Tests.Services;

/// <summary>
/// Unit tests for MountOrchestrator to verify validation, orchestration, and error handling.
/// </summary>
public class MountOrchestratorTests
{
    private readonly Mock<IScriptExecutor> _mockScriptExecutor;
    private readonly Mock<IMountHistoryService> _mockHistoryService;
    private readonly Mock<IOptions<LiMountConfiguration>> _mockConfig;
    private readonly MountOrchestrator _orchestrator;

    public MountOrchestratorTests()
    {
        _mockScriptExecutor = new Mock<IScriptExecutor>();
        _mockHistoryService = new Mock<IMountHistoryService>();
        _mockConfig = new Mock<IOptions<LiMountConfiguration>>();

        // Setup default configuration
        var config = new LiMountConfiguration
        {
            MountOperations = new MountOperationsConfig
            {
                UncAccessibilityRetries = 5,
                UncAccessibilityDelayMs = 100 // Shorter delay for tests
            }
        };
        _mockConfig.Setup(c => c.Value).Returns(config);

        _orchestrator = new MountOrchestrator(
            _mockScriptExecutor.Object,
            _mockConfig.Object,
            _mockHistoryService.Object);
    }

    [Fact]
    public async Task MountAndMapAsync_NegativeDiskIndex_ReturnsValidationError()
    {
        // Act
        var result = await _orchestrator.MountAndMapAsync(-1, 1, 'Z');

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.FailedStep.Should().Be("validation");
        result.ErrorMessage.Should().Contain("Disk index must be non-negative");
    }

    [Fact]
    public async Task MountAndMapAsync_ZeroPartitionNumber_ReturnsValidationError()
    {
        // Act
        var result = await _orchestrator.MountAndMapAsync(1, 0, 'Z');

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.FailedStep.Should().Be("validation");
        result.ErrorMessage.Should().Contain("Partition number must be greater than 0");
    }

    [Fact]
    public async Task MountAndMapAsync_InvalidDriveLetter_ReturnsValidationError()
    {
        // Act
        var result = await _orchestrator.MountAndMapAsync(1, 1, '1'); // Digit instead of letter

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.FailedStep.Should().Be("validation");
        result.ErrorMessage.Should().Contain("Drive letter must be a valid letter");
    }

    [Fact]
    public async Task MountAndMapAsync_EmptyFilesystemType_ReturnsValidationError()
    {
        // Act
        var result = await _orchestrator.MountAndMapAsync(1, 1, 'Z', "");

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.FailedStep.Should().Be("validation");
        result.ErrorMessage.Should().Contain("Filesystem type cannot be empty");
    }

    [Fact]
    public async Task MountAndMapAsync_MountScriptFails_ReturnsFailureWithMountStep()
    {
        // Arrange
        var mountResult = new MountResult
        {
            Success = false,
            ErrorMessage = "WSL mount failed"
        };
        _mockScriptExecutor
            .Setup(e => e.ExecuteMountScriptAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(mountResult);

        // Act
        var result = await _orchestrator.MountAndMapAsync(1, 1, 'Z');

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.FailedStep.Should().Be("mount");
        result.ErrorMessage.Should().Contain("WSL mount failed");
    }

    [Fact]
    public async Task MountAndMapAsync_MappingScriptFails_ReturnsFailureWithMapStep()
    {
        // Arrange
        var mountResult = new MountResult
        {
            Success = true,
            DistroName = "Ubuntu",
            MountPathLinux = "/mnt/wsl/PHYSICALDRIVE1p1",
            MountPathUNC = @"\\wsl$\Ubuntu\mnt\wsl\PHYSICALDRIVE1p1"
        };
        _mockScriptExecutor
            .Setup(e => e.ExecuteMountScriptAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(mountResult);

        var mappingResult = new MappingResult
        {
            Success = false,
            ErrorMessage = "Drive letter already in use"
        };
        _mockScriptExecutor
            .Setup(e => e.ExecuteMappingScriptAsync(It.IsAny<char>(), It.IsAny<string>()))
            .ReturnsAsync(mappingResult);

        // Act
        var result = await _orchestrator.MountAndMapAsync(1, 1, 'Z');

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.FailedStep.Should().Be("map");
        result.ErrorMessage.Should().Contain("Drive letter already in use");
    }

    [Fact]
    public async Task MountAndMapAsync_Success_ReturnsSuccessWithAllDetails()
    {
        // Arrange
        var mountResult = new MountResult
        {
            Success = true,
            DistroName = "Ubuntu",
            MountPathLinux = "/mnt/wsl/PHYSICALDRIVE1p1",
            MountPathUNC = @"\\wsl$\Ubuntu\mnt\wsl\PHYSICALDRIVE1p1"
        };
        _mockScriptExecutor
            .Setup(e => e.ExecuteMountScriptAsync(1, 1, "ext4", null))
            .ReturnsAsync(mountResult);

        var mappingResult = new MappingResult
        {
            Success = true,
            DriveLetter = 'Z',
            UNCPath = @"\\wsl$\Ubuntu\mnt\wsl\PHYSICALDRIVE1p1"
        };
        _mockScriptExecutor
            .Setup(e => e.ExecuteMappingScriptAsync('Z', @"\\wsl$\Ubuntu\mnt\wsl\PHYSICALDRIVE1p1"))
            .ReturnsAsync(mappingResult);

        // Act
        var result = await _orchestrator.MountAndMapAsync(1, 1, 'Z');

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.DiskIndex.Should().Be(1);
        result.Partition.Should().Be(1);
        result.DriveLetter.Should().Be('Z');
        result.DistroName.Should().Be("Ubuntu");
        result.MountPathLinux.Should().Be("/mnt/wsl/PHYSICALDRIVE1p1");
        result.MountPathUNC.Should().Be(@"\\wsl$\Ubuntu\mnt\wsl\PHYSICALDRIVE1p1");
    }

    [Fact]
    public async Task MountAndMapAsync_Success_LogsToHistory()
    {
        // Arrange
        var mountResult = new MountResult
        {
            Success = true,
            DistroName = "Ubuntu",
            MountPathLinux = "/mnt/wsl/PHYSICALDRIVE1p1",
            MountPathUNC = @"\\wsl$\Ubuntu\mnt\wsl\PHYSICALDRIVE1p1"
        };
        _mockScriptExecutor
            .Setup(e => e.ExecuteMountScriptAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(mountResult);

        var mappingResult = new MappingResult { Success = true, DriveLetter = 'Z' };
        _mockScriptExecutor
            .Setup(e => e.ExecuteMappingScriptAsync(It.IsAny<char>(), It.IsAny<string>()))
            .ReturnsAsync(mappingResult);

        // Act
        await _orchestrator.MountAndMapAsync(1, 1, 'Z');

        // Assert
        _mockHistoryService.Verify(
            h => h.AddEntryAsync(It.Is<MountHistoryEntry>(e => e.Success && e.OperationType == "Mount")),
            Times.Once);
    }

    [Fact]
    public async Task MountAndMapAsync_Failure_LogsToHistory()
    {
        // Arrange
        var mountResult = new MountResult { Success = false, ErrorMessage = "Test error" };
        _mockScriptExecutor
            .Setup(e => e.ExecuteMountScriptAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(mountResult);

        // Act
        await _orchestrator.MountAndMapAsync(1, 1, 'Z');

        // Assert
        _mockHistoryService.Verify(
            h => h.AddEntryAsync(It.Is<MountHistoryEntry>(e => !e.Success && e.OperationType == "Mount")),
            Times.Once);
    }
}
