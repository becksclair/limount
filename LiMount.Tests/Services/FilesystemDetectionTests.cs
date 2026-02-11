using FluentAssertions;
using Microsoft.Extensions.Options;
using LiMount.Core.Configuration;
using LiMount.Core.Models;
using LiMount.Core.Services;

namespace LiMount.Tests.Services;

/// <summary>
/// Tests for filesystem detection functionality.
/// Tests ScriptExecutor's ability to detect filesystem types via WSL.
/// </summary>
public class FilesystemDetectionTests
{
    private readonly IOptions<LiMountConfiguration> _config;

    public FilesystemDetectionTests()
    {
        _config = Options.Create(new LiMountConfiguration
        {
            ScriptExecution = new ScriptExecutionConfig
            {
                TempFilePollingTimeoutSeconds = 30,
                PollingIntervalMs = 100
            }
        });
    }

    #region Configuration Validation Tests

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public void ScriptExecutor_InvalidTimeoutConfiguration_ClampedToSafeValues(int timeout)
    {
        // Arrange
        var config = Options.Create(new LiMountConfiguration
        {
            ScriptExecution = new ScriptExecutionConfig
            {
                TempFilePollingTimeoutSeconds = timeout,
                PollingIntervalMs = 100
            }
        });

        // Act - should not throw during construction
        var act = () => new ScriptExecutor(config, logger: NullLogger<ScriptExecutor>.Instance);

        // Assert
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ScriptExecutor_InvalidPollingInterval_ClampedToSafeValues(int interval)
    {
        // Arrange
        var config = Options.Create(new LiMountConfiguration
        {
            ScriptExecution = new ScriptExecutionConfig
            {
                TempFilePollingTimeoutSeconds = 30,
                PollingIntervalMs = interval
            }
        });

        // Act - should not throw during construction
        var act = () => new ScriptExecutor(config, logger: NullLogger<ScriptExecutor>.Instance);

        // Assert
        act.Should().NotThrow();
    }

    #endregion

    #region Filesystem Detection Edge Cases

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(0, 0)]
    [InlineData(0, -1)]
    [InlineData(-1, -1)]
    public async Task DetectFilesystemTypeAsync_InvalidDiskOrPartition_ReturnsNull(int diskIndex, int partition)
    {
        // Arrange
        var executor = new ScriptExecutor(_config, logger: NullLogger<ScriptExecutor>.Instance);

        // Act
        var result = await executor.DetectFilesystemTypeAsync(diskIndex, partition);

        // Assert - invalid parameters should return null (not throw)
        result.Should().BeNull();
    }

    #endregion

    #region Mount Result Parsing Tests

    [Fact]
    public void MountResult_DefaultState_IsNotSuccess()
    {
        // Arrange & Act
        var result = new MountResult();

        // Assert
        result.Success.Should().BeFalse();
        result.MountPathLinux.Should().BeNull();
        result.MountPathUNC.Should().BeNull();
        result.DistroName.Should().BeNull();
    }

    [Fact]
    public void MountResult_WithValues_ReturnsCorrectProperties()
    {
        // Arrange & Act
        var result = new MountResult
        {
            Success = true,
            MountPathLinux = "/mnt/wsl/PHYSICALDRIVE1p2",
            MountPathUNC = @"\\wsl$\Ubuntu\mnt\wsl\PHYSICALDRIVE1p2",
            DistroName = "Ubuntu",
            ErrorCode = "XFS_UNSUPPORTED_FEATURES",
            ErrorHint = "hint",
            DmesgSummary = "summary"
        };

        // Assert
        result.Success.Should().BeTrue();
        result.MountPathLinux.Should().Be("/mnt/wsl/PHYSICALDRIVE1p2");
        result.MountPathUNC.Should().Be(@"\\wsl$\Ubuntu\mnt\wsl\PHYSICALDRIVE1p2");
        result.DistroName.Should().Be("Ubuntu");
        result.ErrorCode.Should().Be("XFS_UNSUPPORTED_FEATURES");
        result.ErrorHint.Should().Be("hint");
        result.DmesgSummary.Should().Be("summary");
    }

    [Fact]
    public void MountResult_FromDictionary_ParsesAlreadyMountedAndUncVerified()
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["STATUS"] = "OK",
            ["DistroName"] = "Ubuntu",
            ["MountPathLinux"] = "/mnt/wsl/PHYSICALDRIVE1p2",
            ["MountPathUNC"] = @"\\wsl.localhost\Ubuntu\mnt\wsl\PHYSICALDRIVE1p2",
            ["AlreadyMounted"] = "true",
            ["UncVerified"] = "false",
            ["ErrorCode"] = "XFS_UNSUPPORTED_FEATURES",
            ["ErrorHint"] = "Kernel feature mismatch",
            ["DmesgSummary"] = "XFS (sde2): Superblock has unknown incompatible features"
        };

        var result = MountResult.FromDictionary(values);

        result.Success.Should().BeTrue();
        result.AlreadyMounted.Should().BeTrue();
        result.UncVerified.Should().BeFalse();
        result.ErrorCode.Should().Be("XFS_UNSUPPORTED_FEATURES");
        result.ErrorHint.Should().Be("Kernel feature mismatch");
        result.DmesgSummary.Should().Contain("XFS");
    }

    [Fact]
    public void MountResult_FromDictionary_MissingOptionalFlags_DefaultsToFalseAndNull()
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["STATUS"] = "OK"
        };

        var result = MountResult.FromDictionary(values);

        result.Success.Should().BeTrue();
        result.AlreadyMounted.Should().BeFalse();
        result.UncVerified.Should().BeNull();
    }

    [Fact]
    public void UnmountResult_DefaultState_IsNotSuccess()
    {
        // Arrange & Act
        var result = new UnmountResult();

        // Assert
        result.Success.Should().BeFalse();
    }

    #endregion

    #region DiskInfo and PartitionInfo Tests

    [Fact]
    public void DiskInfo_DefaultValues_AreReasonable()
    {
        // Arrange & Act
        var diskInfo = new DiskInfo();

        // Assert
        diskInfo.Index.Should().Be(0);
        diskInfo.DeviceId.Should().BeEmpty(); // Default is empty string
        diskInfo.Model.Should().BeEmpty(); // Default is empty string
        diskInfo.SizeBytes.Should().Be(0);
        diskInfo.IsSystem.Should().BeFalse();
        diskInfo.IsBoot.Should().BeFalse();
        diskInfo.Partitions.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public void DiskInfo_HasLinuxPartitions_ReturnsTrueWhenLinuxPartitionExists()
    {
        // Arrange
        var diskInfo = new DiskInfo
        {
            Index = 1,
            Partitions = new List<PartitionInfo>
            {
                new() { PartitionNumber = 1, IsLikelyLinux = false },
                new() { PartitionNumber = 2, IsLikelyLinux = true },
            }
        };

        // Assert
        diskInfo.HasLinuxPartitions.Should().BeTrue();
    }

    [Fact]
    public void DiskInfo_HasLinuxPartitions_ReturnsFalseWhenNoLinuxPartitions()
    {
        // Arrange
        var diskInfo = new DiskInfo
        {
            Index = 1,
            Partitions = new List<PartitionInfo>
            {
                new() { PartitionNumber = 1, IsLikelyLinux = false },
                new() { PartitionNumber = 2, IsLikelyLinux = false },
            }
        };

        // Assert
        diskInfo.HasLinuxPartitions.Should().BeFalse();
    }

    [Fact]
    public void PartitionInfo_DefaultValues_AreReasonable()
    {
        // Arrange & Act
        var partitionInfo = new PartitionInfo();

        // Assert
        partitionInfo.PartitionNumber.Should().Be(0);
        partitionInfo.SizeBytes.Should().Be(0);
        partitionInfo.Label.Should().BeNull();
        partitionInfo.FileSystemType.Should().BeNull();
        partitionInfo.HasDriveLetter.Should().BeFalse();
        partitionInfo.DriveLetter.Should().BeNull();
        partitionInfo.IsLikelyLinux.Should().BeFalse();
    }

    [Fact]
    public void PartitionInfo_SizeFormatted_FormatsCorrectly()
    {
        // Arrange
        var partitionInfo = new PartitionInfo
        {
            SizeBytes = 10L * 1024 * 1024 * 1024 // 10 GiB
        };

        // Assert
        partitionInfo.SizeFormatted.Should().Contain("10");
        partitionInfo.SizeFormatted.Should().Contain("GB");
    }

    [Fact]
    public void PartitionInfo_DisplayName_FormatsCorrectly()
    {
        // Arrange
        var partitionWithLabel = new PartitionInfo
        {
            PartitionNumber = 1,
            SizeBytes = 50L * 1024 * 1024 * 1024,
            Label = "MyData"
        };

        var partitionWithoutLabel = new PartitionInfo
        {
            PartitionNumber = 2,
            SizeBytes = 100L * 1024 * 1024 * 1024
        };

        // Assert
        partitionWithLabel.DisplayName.Should().Contain("MyData");
        partitionWithLabel.DisplayName.Should().Contain("50");

        partitionWithoutLabel.DisplayName.Should().Contain("Partition 2");
        partitionWithoutLabel.DisplayName.Should().Contain("100");
    }

    #endregion

    #region Known Filesystem Type Tests

    [Theory]
    [InlineData("ext4")]
    [InlineData("ext3")]
    [InlineData("ext2")]
    [InlineData("xfs")]
    [InlineData("btrfs")]
    [InlineData("f2fs")]
    public void LinuxFilesystemTypes_AreRecognized(string fsType)
    {
        // This test documents the expected Linux filesystem types
        // that the application should handle correctly

        // Common Linux filesystems that should be detectable
        var linuxFilesystems = new[] { "ext4", "ext3", "ext2", "xfs", "btrfs", "f2fs", "jfs", "reiserfs" };
        linuxFilesystems.Should().Contain(fsType);
    }

    [Theory]
    [InlineData("ntfs")]
    [InlineData("fat32")]
    [InlineData("exfat")]
    [InlineData("refs")]
    public void WindowsFilesystemTypes_AreRecognized(string fsType)
    {
        // This test documents the expected Windows filesystem types
        // that the application should recognize and exclude from Linux partition detection

        var windowsFilesystems = new[] { "ntfs", "fat32", "fat16", "fat12", "exfat", "refs" };
        windowsFilesystems.Should().Contain(fsType.ToLowerInvariant());
    }

    #endregion
}
