using FluentAssertions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using LiMount.Core.Configuration;
using LiMount.Core.Models;
using LiMount.Core.Services;

namespace LiMount.Tests.Services;

/// <summary>
/// Unit tests for MountStateService to verify state persistence and reconciliation.
/// </summary>
public class MountStateServiceTests : IDisposable
{
    private readonly string _testStateFilePath;
    private readonly MountStateService _service;

    public MountStateServiceTests()
    {
        // Create a temporary file path for testing
        _testStateFilePath = Path.Combine(Path.GetTempPath(), $"limount_test_state_{Guid.NewGuid()}.json");

        var config = new LiMountConfiguration
        {
            History = new HistoryConfig
            {
                StateFilePath = _testStateFilePath
            }
        };

        var options = Options.Create(config);
        _service = new MountStateService(NullLogger<MountStateService>.Instance, _testStateFilePath);
    }

    public void Dispose()
    {
        // Clean up test file
        if (File.Exists(_testStateFilePath))
        {
            File.Delete(_testStateFilePath);
        }
    }

    [Fact]
    public async Task GetActiveMountsAsync_WhenEmpty_ReturnsEmptyList()
    {
        // Act
        var mounts = await _service.GetActiveMountsAsync();

        // Assert
        mounts.Should().NotBeNull();
        mounts.Should().BeEmpty();
    }

    [Fact]
    public async Task RegisterMountAsync_AddsMount_ToPersistentState()
    {
        // Arrange
        var mount = new ActiveMount
        {
            Id = Guid.NewGuid().ToString(),
            MountedAt = DateTime.Now,
            DiskIndex = 1,
            PartitionNumber = 1,
            DriveLetter = 'Z',
            DistroName = "Ubuntu",
            MountPathLinux = "/mnt/wsl/PHYSICALDRIVE1p1",
            MountPathUNC = @"\\wsl$\Ubuntu\mnt\wsl\PHYSICALDRIVE1p1",
            IsVerified = true,
            LastVerified = DateTime.Now
        };

        // Act
        await _service.RegisterMountAsync(mount);

        // Assert
        var mounts = await _service.GetActiveMountsAsync();
        mounts.Should().ContainSingle();
        mounts[0].DiskIndex.Should().Be(1);
        mounts[0].DriveLetter.Should().Be('Z');
    }

    [Fact]
    public async Task RegisterMountAsync_PersistsToFile()
    {
        // Arrange
        var mount = new ActiveMount
        {
            Id = Guid.NewGuid().ToString(),
            MountedAt = DateTime.Now,
            DiskIndex = 2,
            PartitionNumber = 3,
            DriveLetter = 'Y',
            DistroName = "Debian",
            MountPathLinux = "/mnt/wsl/PHYSICALDRIVE2p3",
            MountPathUNC = @"\\wsl$\Debian\mnt\wsl\PHYSICALDRIVE2p3"
        };

        // Act
        await _service.RegisterMountAsync(mount);

        // Assert - File should exist and contain data
        File.Exists(_testStateFilePath).Should().BeTrue();

        var fileContent = await File.ReadAllTextAsync(_testStateFilePath);
        fileContent.Should().Contain("PHYSICALDRIVE2p3");
        fileContent.Should().Contain("Debian");
    }

    [Fact]
    public async Task UnregisterMountAsync_RemovesMount_FromState()
    {
        // Arrange
        var mount = new ActiveMount
        {
            Id = Guid.NewGuid().ToString(),
            DiskIndex = 5,
            PartitionNumber = 1,
            DriveLetter = 'X'
        };
        await _service.RegisterMountAsync(mount);

        // Act
        await _service.UnregisterMountAsync(5);

        // Assert
        var mounts = await _service.GetActiveMountsAsync();
        mounts.Should().BeEmpty();
    }

    [Fact]
    public async Task GetMountForDiskAsync_WhenExists_ReturnsMountInfo()
    {
        // Arrange
        var mount = new ActiveMount
        {
            Id = Guid.NewGuid().ToString(),
            DiskIndex = 7,
            PartitionNumber = 2,
            DriveLetter = 'W',
            DistroName = "Ubuntu"
        };
        await _service.RegisterMountAsync(mount);

        // Act
        var result = await _service.GetMountForDiskAsync(7);

        // Assert
        result.Should().NotBeNull();
        result!.DiskIndex.Should().Be(7);
        result.PartitionNumber.Should().Be(2);
        result.DriveLetter.Should().Be('W');
    }

    [Fact]
    public async Task GetMountForDiskAsync_WhenNotExists_ReturnsNull()
    {
        // Act
        var result = await _service.GetMountForDiskAsync(999);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetMountForDriveLetterAsync_WhenExists_ReturnsMountInfo()
    {
        // Arrange
        var mount = new ActiveMount
        {
            Id = Guid.NewGuid().ToString(),
            DiskIndex = 3,
            PartitionNumber = 1,
            DriveLetter = 'V'
        };
        await _service.RegisterMountAsync(mount);

        // Act
        var result = await _service.GetMountForDriveLetterAsync('V');

        // Assert
        result.Should().NotBeNull();
        result!.DriveLetter.Should().Be('V');
        result.DiskIndex.Should().Be(3);
    }

    [Fact]
    public async Task IsDiskMountedAsync_WhenMounted_ReturnsTrue()
    {
        // Arrange
        var mount = new ActiveMount
        {
            Id = Guid.NewGuid().ToString(),
            DiskIndex = 10,
            PartitionNumber = 1,
            DriveLetter = 'U'
        };
        await _service.RegisterMountAsync(mount);

        // Act
        var result = await _service.IsDiskMountedAsync(10);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task IsDiskMountedAsync_WhenNotMounted_ReturnsFalse()
    {
        // Act
        var result = await _service.IsDiskMountedAsync(999);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task RegisterMountAsync_MultipleMounts_AllPersisted()
    {
        // Arrange
        var mount1 = new ActiveMount { Id = Guid.NewGuid().ToString(), DiskIndex = 1, PartitionNumber = 1, DriveLetter = 'Z' };
        var mount2 = new ActiveMount { Id = Guid.NewGuid().ToString(), DiskIndex = 2, PartitionNumber = 1, DriveLetter = 'Y' };
        var mount3 = new ActiveMount { Id = Guid.NewGuid().ToString(), DiskIndex = 3, PartitionNumber = 1, DriveLetter = 'X' };

        // Act
        await _service.RegisterMountAsync(mount1);
        await _service.RegisterMountAsync(mount2);
        await _service.RegisterMountAsync(mount3);

        // Assert
        var mounts = await _service.GetActiveMountsAsync();
        mounts.Should().HaveCount(3);
        mounts.Select(m => m.DriveLetter).Should().Contain(new[] { 'Z', 'Y', 'X' });
    }

    [Fact]
    public async Task UnregisterMountAsync_NonExistentDisk_DoesNotThrow()
    {
        // Act
        Func<Task> act = async () => await _service.UnregisterMountAsync(999);

        // Assert
        await act.Should().NotThrowAsync();
    }
}
