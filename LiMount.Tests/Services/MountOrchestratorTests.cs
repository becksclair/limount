using System.IO;
using System.Reflection;
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
    private readonly Mock<IMountScriptService> _mockMountScriptService;
    private readonly Mock<IDriveMappingService> _mockDriveMappingService;
    private readonly Mock<IMountHistoryService> _mockHistoryService;
    private readonly Mock<IMountStateService> _mockMountStateService;
    private readonly Mock<IOptions<LiMountConfiguration>> _mockConfig;
    private readonly MountOrchestrator _orchestrator;
    private readonly string _existingUncPath;

    public MountOrchestratorTests()
    {
        _mockMountScriptService = new Mock<IMountScriptService>();
        _mockDriveMappingService = new Mock<IDriveMappingService>();
        _mockHistoryService = new Mock<IMountHistoryService>();
        _mockMountStateService = new Mock<IMountStateService>();
        _mockConfig = new Mock<IOptions<LiMountConfiguration>>();
        _existingUncPath = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        // Setup default configuration
        var config = new LiMountConfiguration
        {
            MountOperations = new MountOperationsConfig
            {
                UncAccessibilityRetries = 1,
                UncAccessibilityDelayMs = 50 // Shorter delay for tests
            }
        };
        _mockConfig.Setup(c => c.Value).Returns(config);

        _mockMountScriptService
            .Setup(e => e.ExecuteUnmountScriptAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UnmountResult { Success = true });

        _orchestrator = new MountOrchestrator(
            _mockMountScriptService.Object,
            _mockDriveMappingService.Object,
            _mockConfig.Object,
            _mockHistoryService.Object,
            _mockMountStateService.Object);
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
        _mockMountScriptService
            .Setup(e => e.ExecuteMountScriptAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
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
            MountPathUNC = _existingUncPath
        };
        _mockMountScriptService
            .Setup(e => e.ExecuteMountScriptAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mountResult);

        var mappingResult = new MappingResult
        {
            Success = false,
            ErrorMessage = "Drive letter already in use"
        };
        _mockDriveMappingService
            .Setup(e => e.ExecuteMappingScriptAsync(It.IsAny<char>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mappingResult);

        // Act
        var result = await _orchestrator.MountAndMapAsync(1, 1, 'Z');

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.FailedStep.Should().Be("map");
        result.ErrorMessage.Should().Contain("Drive letter already in use");
        _mockMountScriptService.Verify(
            s => s.ExecuteUnmountScriptAsync(1, It.IsAny<CancellationToken>()),
            Times.Once);
        _mockMountStateService.Verify(
            s => s.UnregisterMountAsync(1, 1, It.IsAny<CancellationToken>()),
            Times.Once);
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
            MountPathUNC = _existingUncPath
        };
        _mockMountScriptService
            .Setup(e => e.ExecuteMountScriptAsync(1, 1, "ext4", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(mountResult);

        var mappingResult = new MappingResult
        {
            Success = true,
            DriveLetter = "Z",
            TargetUNC = _existingUncPath
        };
        _mockDriveMappingService
            .Setup(e => e.ExecuteMappingScriptAsync('Z', _existingUncPath, It.IsAny<CancellationToken>()))
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
        result.MountPathUNC.Should().Be(_existingUncPath);
        _mockMountStateService.Verify(
            s => s.RegisterMountAsync(
                It.Is<ActiveMount>(m =>
                    m.DiskIndex == 1 &&
                    m.PartitionNumber == 1 &&
                    m.DriveLetter == 'Z' &&
                    m.MountPathUNC == _existingUncPath),
                It.IsAny<CancellationToken>()),
            Times.Once);
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
            MountPathUNC = _existingUncPath
        };
        _mockMountScriptService
            .Setup(e => e.ExecuteMountScriptAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mountResult);

        var mappingResult = new MappingResult { Success = true, DriveLetter = "Z" };
        _mockDriveMappingService
            .Setup(e => e.ExecuteMappingScriptAsync(It.IsAny<char>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mappingResult);

        // Act
        await _orchestrator.MountAndMapAsync(1, 1, 'Z');

        // Assert
        _mockHistoryService.Verify(
            h => h.AddEntryAsync(
                It.Is<MountHistoryEntry>(e => e.Success && e.OperationType == MountHistoryOperationType.Mount),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task MountAndMapAsync_Failure_LogsToHistory()
    {
        // Arrange
        var mountResult = new MountResult { Success = false, ErrorMessage = "Test error" };
        _mockMountScriptService
            .Setup(e => e.ExecuteMountScriptAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mountResult);

        // Act
        await _orchestrator.MountAndMapAsync(1, 1, 'Z');

        // Assert
        _mockHistoryService.Verify(
            h => h.AddEntryAsync(
                It.Is<MountHistoryEntry>(e => !e.Success && e.OperationType == MountHistoryOperationType.Mount),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task MountAndMapAsync_MountReturnsNoUnc_RollsBackAndFails()
    {
        // Arrange
        var mountResult = new MountResult
        {
            Success = true,
            DistroName = "Ubuntu",
            MountPathLinux = "/mnt/wsl/PHYSICALDRIVE1p1",
            MountPathUNC = null
        };

        _mockMountScriptService
            .Setup(e => e.ExecuteMountScriptAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mountResult);

        // Act
        var result = await _orchestrator.MountAndMapAsync(1, 1, 'Z');

        // Assert
        result.Success.Should().BeFalse();
        result.FailedStep.Should().Be("mount");
        result.ErrorMessage.Should().Contain("no UNC path");
        _mockDriveMappingService.Verify(
            m => m.ExecuteMappingScriptAsync(It.IsAny<char>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _mockMountScriptService.Verify(
            s => s.ExecuteUnmountScriptAsync(1, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task MountAndMapAsync_MappingFailure_RollbackFailureAddsContext()
    {
        // Arrange
        var mountResult = new MountResult
        {
            Success = true,
            DistroName = "Ubuntu",
            MountPathLinux = "/mnt/wsl/PHYSICALDRIVE1p1",
            MountPathUNC = _existingUncPath
        };
        _mockMountScriptService
            .Setup(e => e.ExecuteMountScriptAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mountResult);

        var mappingResult = new MappingResult
        {
            Success = false,
            ErrorMessage = "Drive letter already in use"
        };
        _mockDriveMappingService
            .Setup(e => e.ExecuteMappingScriptAsync(It.IsAny<char>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mappingResult);

        _mockMountScriptService
            .Setup(e => e.ExecuteUnmountScriptAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UnmountResult { Success = false, ErrorMessage = "Rollback failed" });

        // Act
        var result = await _orchestrator.MountAndMapAsync(1, 1, 'Z');

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Drive letter already in use");
        result.ErrorMessage.Should().Contain("cleanup");
        result.ErrorMessage.Should().Contain("Rollback failed");
        _mockMountScriptService.Verify(
            s => s.ExecuteUnmountScriptAsync(1, It.IsAny<CancellationToken>()),
            Times.Once);
        _mockMountStateService.Verify(
            s => s.UnregisterMountAsync(1, 1, It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task MountAndMapAsync_MappingFailure_SkipsRollbackWhenPartitionAlreadyTracked()
    {
        // Arrange
        var preExistingMount = new ActiveMount
        {
            DiskIndex = 1,
            PartitionNumber = 1,
            MountPathUNC = @"\\wsl$\Other\mnt\wsl\PHYSICALDRIVE1p1"
        };
        _mockMountStateService
            .Setup(s => s.GetMountForDiskPartitionAsync(1, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(preExistingMount);

        var mountResult = new MountResult
        {
            Success = true,
            DistroName = "Ubuntu",
            MountPathLinux = "/mnt/wsl/PHYSICALDRIVE1p1",
            MountPathUNC = _existingUncPath
        };
        _mockMountScriptService
            .Setup(e => e.ExecuteMountScriptAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mountResult);

        var mappingResult = new MappingResult
        {
            Success = false,
            ErrorMessage = "Drive letter already in use"
        };
        _mockDriveMappingService
            .Setup(e => e.ExecuteMappingScriptAsync(It.IsAny<char>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mappingResult);

        // Act
        var result = await _orchestrator.MountAndMapAsync(1, 1, 'Z');

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("cleanup");
        result.ErrorMessage.Should().Contain("skipped");
        _mockMountScriptService.Verify(
            s => s.ExecuteUnmountScriptAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _mockMountStateService.Verify(
            s => s.UnregisterMountAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task MountAndMapAsync_MappingFailure_SkipsRollbackWhenMountWasAlreadyPresentInWsl()
    {
        var mountResult = new MountResult
        {
            Success = true,
            AlreadyMounted = true,
            DistroName = "Ubuntu",
            MountPathLinux = "/mnt/wsl/PHYSICALDRIVE1p1",
            MountPathUNC = _existingUncPath
        };
        _mockMountScriptService
            .Setup(e => e.ExecuteMountScriptAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mountResult);

        _mockDriveMappingService
            .Setup(e => e.ExecuteMappingScriptAsync(It.IsAny<char>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MappingResult
            {
                Success = false,
                ErrorMessage = "Drive letter already in use"
            });

        var result = await _orchestrator.MountAndMapAsync(1, 1, 'Z');

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("cleanup");
        result.ErrorMessage.Should().Contain("already mounted");
        _mockMountScriptService.Verify(
            s => s.ExecuteUnmountScriptAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _mockMountStateService.Verify(
            s => s.UnregisterMountAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task MountAndMapAsync_UncInaccessibleBeforeMapping_RollsBack()
    {
        // Arrange
        var mountResult = new MountResult
        {
            Success = true,
            DistroName = "Ubuntu",
            MountPathLinux = "/mnt/wsl/PHYSICALDRIVE1p1",
            MountPathUNC = @"\\wsl$\Ubuntu\mnt\wsl\_nonexistent_path"
        };
        _mockMountScriptService
            .Setup(e => e.ExecuteMountScriptAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mountResult);

        // Act
        var result = await _orchestrator.MountAndMapAsync(1, 1, 'Z');

        // Assert
        result.Success.Should().BeFalse();
        result.FailedStep.Should().Be("mount");
        result.ErrorMessage.Should().Contain("not accessible");
        _mockMountScriptService.Verify(
            s => s.ExecuteUnmountScriptAsync(1, It.IsAny<CancellationToken>()),
            Times.Once);
        _mockMountStateService.Verify(
            s => s.UnregisterMountAsync(1, 1, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task MountAndMapAsync_DriveLetterConflict_FailsFast()
    {
        // Arrange
        var conflictingMount = new ActiveMount
        {
            DiskIndex = 9,
            PartitionNumber = 9,
            DriveLetter = 'Z',
            MountPathUNC = @"\\wsl$\Other\mnt\wsl\PHYSICALDRIVE9p9"
        };
        _mockMountStateService
            .Setup(s => s.GetMountForDriveLetterAsync('Z', It.IsAny<CancellationToken>()))
            .ReturnsAsync(conflictingMount);

        // Act
        var result = await _orchestrator.MountAndMapAsync(1, 1, 'Z');

        // Assert
        result.Success.Should().BeFalse();
        result.FailedStep.Should().Be("validation");
        result.ErrorMessage.Should().Contain("already mapped");
        _mockMountScriptService.Verify(
            s => s.ExecuteMountScriptAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _mockDriveMappingService.Verify(
            m => m.ExecuteMappingScriptAsync(It.IsAny<char>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _mockMountStateService.Verify(
            s => s.RegisterMountAsync(It.IsAny<ActiveMount>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task MountAndMapAsync_IdempotentWhenDriveAlreadyMappedToSameTarget()
    {
        // Arrange
        var existingMount = new ActiveMount
        {
            DiskIndex = 1,
            PartitionNumber = 1,
            DriveLetter = 'Z',
            MountPathLinux = "/mnt/wsl/PHYSICALDRIVE1p1",
            MountPathUNC = _existingUncPath,
            DistroName = "Ubuntu"
        };
        _mockMountStateService
            .Setup(s => s.GetMountForDriveLetterAsync('Z', It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingMount);

        // Act
        var result = await _orchestrator.MountAndMapAsync(1, 1, 'Z');

        // Assert
        result.Success.Should().BeTrue();
        result.MountPathUNC.Should().Be(_existingUncPath);
        result.DriveLetter.Should().Be('Z');
        _mockMountScriptService.Verify(
            s => s.ExecuteMountScriptAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _mockDriveMappingService.Verify(
            m => m.ExecuteMappingScriptAsync(It.IsAny<char>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _mockHistoryService.Verify(
            h => h.AddEntryAsync(It.IsAny<MountHistoryEntry>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _mockMountStateService.Verify(
            s => s.RegisterMountAsync(It.IsAny<ActiveMount>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public void Constructor_UsesDedicatedUncExistenceTimeoutSetting()
    {
        var config = Options.Create(new LiMountConfiguration
        {
            MountOperations = new MountOperationsConfig
            {
                UncAccessibilityRetries = 1,
                UncAccessibilityDelayMs = 777,
                UncExistenceCheckTimeoutMs = 2345
            }
        });

        var orchestrator = new MountOrchestrator(
            _mockMountScriptService.Object,
            _mockDriveMappingService.Object,
            config,
            _mockHistoryService.Object,
            _mockMountStateService.Object);

        var timeoutField = typeof(MountOrchestrator).GetField("_uncExistenceTimeoutMs", BindingFlags.Instance | BindingFlags.NonPublic);
        timeoutField.Should().NotBeNull();
        timeoutField!.GetValue(orchestrator).Should().Be(2345);
    }
}
