using Moq;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;
using LiMount.Core.Interfaces;
using LiMount.Core.Models;
using LiMount.Core.Services;

namespace LiMount.Tests.Services;

/// <summary>
/// Unit tests for UnmountOrchestrator to verify validation, orchestration, and error handling.
/// </summary>
public class UnmountOrchestratorTests
{
    private readonly Mock<IMountScriptService> _mockMountScriptService;
    private readonly Mock<IDriveMappingService> _mockDriveMappingService;
    private readonly Mock<IMountHistoryService> _mockHistoryService;
    private readonly UnmountOrchestrator _orchestrator;

    public UnmountOrchestratorTests()
    {
        _mockMountScriptService = new Mock<IMountScriptService>();
        _mockDriveMappingService = new Mock<IDriveMappingService>();
        _mockHistoryService = new Mock<IMountHistoryService>();
        _orchestrator = new UnmountOrchestrator(
            _mockMountScriptService.Object,
            _mockDriveMappingService.Object,
            _mockHistoryService.Object);
    }

    [Fact]
    public async Task UnmountAndUnmapAsync_NegativeDiskIndex_ReturnsValidationError()
    {
        // Act
        var result = await _orchestrator.UnmountAndUnmapAsync(-1, 'Z');

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.FailedStep.Should().Be("validation");
        result.ErrorMessage.Should().Contain("Disk index must be non-negative");
    }

    [Fact]
    public async Task UnmountAndUnmapAsync_InvalidDriveLetter_ReturnsValidationError()
    {
        // Act
        var result = await _orchestrator.UnmountAndUnmapAsync(1, '1'); // Digit instead of letter

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.FailedStep.Should().Be("validation");
        result.ErrorMessage.Should().Contain("Drive letter must be a valid letter");
    }

    [Fact]
    public async Task UnmountAndUnmapAsync_UnmappingFails_ContinuesToUnmountButReturnsFailure()
    {
        // Arrange
        var unmappingResult = new UnmappingResult
        {
            Success = false,
            ErrorMessage = "Drive letter unmapping failed"
        };
        _mockDriveMappingService
            .Setup(e => e.ExecuteUnmappingScriptAsync(It.IsAny<char>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(unmappingResult);

        var unmountResult = new UnmountResult
        {
            Success = true
        };
        _mockMountScriptService
            .Setup(e => e.ExecuteUnmountScriptAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(unmountResult);

        // Act
        var result = await _orchestrator.UnmountAndUnmapAsync(1, 'Z');

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.FailedStep.Should().Be("unmap");
        result.ErrorMessage.Should().Contain("Drive letter unmapping failed");
        _mockMountScriptService.Verify(e => e.ExecuteUnmountScriptAsync(1, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UnmountAndUnmapAsync_UnmountFails_ReturnsFailureWithUnmountStep()
    {
        // Arrange
        var unmappingResult = new UnmappingResult
        {
            Success = true,
            DriveLetter = "Z"
        };
        _mockDriveMappingService
            .Setup(e => e.ExecuteUnmappingScriptAsync(It.IsAny<char>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(unmappingResult);

        var unmountResult = new UnmountResult
        {
            Success = false,
            ErrorMessage = "Unmount operation failed"
        };
        _mockMountScriptService
            .Setup(e => e.ExecuteUnmountScriptAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(unmountResult);

        // Act
        var result = await _orchestrator.UnmountAndUnmapAsync(1, 'Z');

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.FailedStep.Should().Be("unmount");
        result.ErrorMessage.Should().Contain("Unmount operation failed");
    }

    [Fact]
    public async Task UnmountAndUnmapAsync_UnmountFileNotFound_TreatedAsSuccess()
    {
        // Arrange
        var unmappingResult = new UnmappingResult
        {
            Success = true,
            DriveLetter = "Z"
        };
        _mockDriveMappingService
            .Setup(e => e.ExecuteUnmappingScriptAsync(It.IsAny<char>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(unmappingResult);

        var unmountResult = new UnmountResult
        {
            Success = false,
            ErrorMessage = "wsl --unmount failed (exit code -1): The system cannot find the file specified. Error code: Wsl/Service/DetachDisk/ERROR_FILE_NOT_FOUND"
        };
        _mockMountScriptService
            .Setup(e => e.ExecuteUnmountScriptAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(unmountResult);

        // Act
        var result = await _orchestrator.UnmountAndUnmapAsync(1, 'Z');

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.FailedStep.Should().BeNull();
        result.ErrorMessage.Should().BeNull();
        result.DriveLetter.Should().Be('Z');
    }

    [Fact]
    public async Task UnmountAndUnmapAsync_Success_ReturnsSuccessWithDetails()
    {
        // Arrange
        var unmappingResult = new UnmappingResult
        {
            Success = true,
            DriveLetter = "Z"
        };
        _mockDriveMappingService
            .Setup(e => e.ExecuteUnmappingScriptAsync('Z', It.IsAny<CancellationToken>()))
            .ReturnsAsync(unmappingResult);

        var unmountResult = new UnmountResult
        {
            Success = true
        };
        _mockMountScriptService
            .Setup(e => e.ExecuteUnmountScriptAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(unmountResult);

        // Act
        var result = await _orchestrator.UnmountAndUnmapAsync(1, 'Z');

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.DiskIndex.Should().Be(1);
        result.DriveLetter.Should().Be('Z');
        result.FailedStep.Should().BeNull();
        result.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public async Task UnmountAndUnmapAsync_Success_LogsToHistory()
    {
        // Arrange
        var unmappingResult = new UnmappingResult { Success = true, DriveLetter = "Z" };
        _mockDriveMappingService
            .Setup(e => e.ExecuteUnmappingScriptAsync(It.IsAny<char>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(unmappingResult);

        var unmountResult = new UnmountResult { Success = true };
        _mockMountScriptService
            .Setup(e => e.ExecuteUnmountScriptAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(unmountResult);

        // Act
        await _orchestrator.UnmountAndUnmapAsync(1, 'Z');

        // Assert
        _mockHistoryService.Verify(
            h => h.AddEntryAsync(
                It.Is<MountHistoryEntry>(e => e.Success && e.OperationType == MountHistoryOperationType.Unmount),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task UnmountAndUnmapAsync_Failure_LogsToHistory()
    {
        // Arrange
        var unmappingResult = new UnmappingResult { Success = false, ErrorMessage = "Test error" };
        _mockDriveMappingService
            .Setup(e => e.ExecuteUnmappingScriptAsync(It.IsAny<char>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(unmappingResult);

        var unmountResult = new UnmountResult { Success = true };
        _mockMountScriptService
            .Setup(e => e.ExecuteUnmountScriptAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(unmountResult);

        // Act
        await _orchestrator.UnmountAndUnmapAsync(1, 'Z');

        // Assert
        _mockHistoryService.Verify(
            h => h.AddEntryAsync(
                It.Is<MountHistoryEntry>(e => !e.Success && e.OperationType == MountHistoryOperationType.Unmount),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
