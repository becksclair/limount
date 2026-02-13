using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using LiMount.Core.Configuration;
using LiMount.Core.Interfaces;
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
    private readonly TestDriveLetterService _driveLetterService;

    public MountStateServiceTests()
    {
        // Create a temporary file path for testing
        _testStateFilePath = Path.Combine(Path.GetTempPath(), $"limount_test_state_{Guid.NewGuid()}.json");
        _driveLetterService = new TestDriveLetterService(new[] { 'Z', 'Y', 'X' });
        _service = CreateService(_driveLetterService);
    }

    public void Dispose()
    {
        _service.Dispose();

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
        await _service.UnregisterMountAsync(5, 1);

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
    public async Task GetMountForDiskPartitionAsync_WhenExists_ReturnsExactPartition()
    {
        await _service.RegisterMountAsync(new ActiveMount
        {
            Id = Guid.NewGuid().ToString(),
            DiskIndex = 7,
            PartitionNumber = 1,
            DriveLetter = 'W'
        });
        await _service.RegisterMountAsync(new ActiveMount
        {
            Id = Guid.NewGuid().ToString(),
            DiskIndex = 7,
            PartitionNumber = 2,
            DriveLetter = 'V'
        });

        var result = await _service.GetMountForDiskPartitionAsync(7, 2);

        result.Should().NotBeNull();
        result!.PartitionNumber.Should().Be(2);
        result.DriveLetter.Should().Be('V');
    }

    [Fact]
    public async Task GetMountsForDiskAsync_WhenMultiplePartitions_ReturnsAllForDisk()
    {
        await _service.RegisterMountAsync(new ActiveMount
        {
            Id = Guid.NewGuid().ToString(),
            DiskIndex = 2,
            PartitionNumber = 1,
            DriveLetter = 'Z'
        });
        await _service.RegisterMountAsync(new ActiveMount
        {
            Id = Guid.NewGuid().ToString(),
            DiskIndex = 2,
            PartitionNumber = 2,
            DriveLetter = 'Y'
        });
        await _service.RegisterMountAsync(new ActiveMount
        {
            Id = Guid.NewGuid().ToString(),
            DiskIndex = 3,
            PartitionNumber = 1,
            DriveLetter = 'X'
        });

        var disk2Mounts = await _service.GetMountsForDiskAsync(2);

        disk2Mounts.Should().HaveCount(2);
        disk2Mounts.Should().OnlyContain(m => m.DiskIndex == 2);
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
        mounts.Select(m => m.DriveLetter).Should().Contain(new char?[] { 'Z', 'Y', 'X' });
    }

    [Fact]
    public async Task RegisterMountAsync_MultiplePartitionsSameDisk_BothPersisted()
    {
        // Arrange - Two different partitions on the same disk
        var partition1 = new ActiveMount
        {
            Id = Guid.NewGuid().ToString(),
            DiskIndex = 2,
            PartitionNumber = 1,
            DriveLetter = 'Z',
            MountPathLinux = "/mnt/wsl/PHYSICALDRIVE2p1"
        };
        var partition2 = new ActiveMount
        {
            Id = Guid.NewGuid().ToString(),
            DiskIndex = 2,
            PartitionNumber = 2,
            DriveLetter = 'Y',
            MountPathLinux = "/mnt/wsl/PHYSICALDRIVE2p2"
        };

        // Act - Register both partitions
        await _service.RegisterMountAsync(partition1);
        await _service.RegisterMountAsync(partition2);

        // Assert - Both should exist
        var mounts = await _service.GetActiveMountsAsync();
        mounts.Should().HaveCount(2);
        mounts.Should().Contain(m => m.DiskIndex == 2 && m.PartitionNumber == 1 && m.DriveLetter == 'Z');
        mounts.Should().Contain(m => m.DiskIndex == 2 && m.PartitionNumber == 2 && m.DriveLetter == 'Y');
    }

    [Fact]
    public async Task RegisterMountAsync_SamePartitionTwice_ReplacesExisting()
    {
        // Arrange - Same partition registered twice with different drive letters
        var firstRegistration = new ActiveMount
        {
            Id = Guid.NewGuid().ToString(),
            DiskIndex = 2,
            PartitionNumber = 1,
            DriveLetter = 'Z',
            MountPathLinux = "/mnt/wsl/PHYSICALDRIVE2p1"
        };
        var secondRegistration = new ActiveMount
        {
            Id = Guid.NewGuid().ToString(),
            DiskIndex = 2,
            PartitionNumber = 1,
            DriveLetter = 'Y', // Different drive letter
            MountPathLinux = "/mnt/wsl/PHYSICALDRIVE2p1"
        };

        // Act - Register same partition twice
        await _service.RegisterMountAsync(firstRegistration);
        await _service.RegisterMountAsync(secondRegistration);

        // Assert - Only one mount should exist, with the second drive letter
        var mounts = await _service.GetActiveMountsAsync();
        mounts.Should().ContainSingle();
        mounts[0].DiskIndex.Should().Be(2);
        mounts[0].PartitionNumber.Should().Be(1);
        mounts[0].DriveLetter.Should().Be('Y'); // Should be the second registration
    }

    [Fact]
    public async Task UnregisterMountAsync_PartitionScoped_LeavesSiblingPartitionIntact()
    {
        var partition1 = new ActiveMount
        {
            Id = Guid.NewGuid().ToString(),
            DiskIndex = 2,
            PartitionNumber = 1,
            DriveLetter = 'Z'
        };
        var partition2 = new ActiveMount
        {
            Id = Guid.NewGuid().ToString(),
            DiskIndex = 2,
            PartitionNumber = 2,
            DriveLetter = 'Y'
        };

        await _service.RegisterMountAsync(partition1);
        await _service.RegisterMountAsync(partition2);

        await _service.UnregisterMountAsync(2, 1);

        var mounts = await _service.GetActiveMountsAsync();
        mounts.Should().ContainSingle();
        mounts[0].DiskIndex.Should().Be(2);
        mounts[0].PartitionNumber.Should().Be(2);
    }

    [Fact]
    public async Task UnregisterDiskAsync_RemovesAllPartitionsForDisk()
    {
        // Arrange - Two partitions on the same disk
        var partition1 = new ActiveMount
        {
            Id = Guid.NewGuid().ToString(),
            DiskIndex = 2,
            PartitionNumber = 1,
            DriveLetter = 'Z'
        };
        var partition2 = new ActiveMount
        {
            Id = Guid.NewGuid().ToString(),
            DiskIndex = 2,
            PartitionNumber = 2,
            DriveLetter = 'Y'
        };

        await _service.RegisterMountAsync(partition1);
        await _service.RegisterMountAsync(partition2);

        // Verify both are registered
        var beforeUnregister = await _service.GetActiveMountsAsync();
        beforeUnregister.Should().HaveCount(2);

        // Act - Unregister the entire disk
        await _service.UnregisterDiskAsync(2);

        // Assert - Both partitions should be removed (WSL unmounts entire disk)
        var afterUnregister = await _service.GetActiveMountsAsync();
        afterUnregister.Should().BeEmpty();
    }

    [Fact]
    public async Task ReconcileMountStateAsync_RemovesOrphanWhenDriveLetterNoLongerMapped()
    {
        var customStateFile = Path.Combine(Path.GetTempPath(), $"limount_reconcile_{Guid.NewGuid()}.json");
        var driveLetterService = new TestDriveLetterService(Array.Empty<char>());
        var service = CreateService(driveLetterService, customStateFile);

        var mount = new ActiveMount
        {
            DiskIndex = 21,
            AccessMode = WindowsAccessMode.DriveLetterLegacy,
            DriveLetter = 'Q'
        };

        await service.RegisterMountAsync(mount);

        var orphaned = await service.ReconcileMountStateAsync();

        orphaned.Should().ContainSingle().Which.DriveLetter.Should().Be('Q');
        (await service.GetActiveMountsAsync()).Should().BeEmpty();

        service.Dispose();

        if (File.Exists(customStateFile))
        {
            File.Delete(customStateFile);
        }
    }

    [Fact]
    public async Task ReconcileMountStateAsync_VerifiesEachPartitionIndependently()
    {
        var stateFile = Path.Combine(Path.GetTempPath(), $"limount_reconcile_multi_{Guid.NewGuid()}.json");
        var accessibleDir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"limount_access_{Guid.NewGuid()}")).FullName;
        var inaccessibleDir = Path.Combine(Path.GetTempPath(), $"limount_missing_{Guid.NewGuid()}");

        var driveLetterService = new TestDriveLetterService(new[] { 'Z', 'Y' });
        var service = CreateService(driveLetterService, stateFile);

        var mounts = new List<ActiveMount>
        {
            new()
            {
                Id = "mount-1",
                DiskIndex = 42,
                PartitionNumber = 1,
                AccessMode = WindowsAccessMode.DriveLetterLegacy,
                DriveLetter = 'Z',
                MountPathUNC = accessibleDir
            },
            new()
            {
                Id = "mount-2",
                DiskIndex = 42,
                PartitionNumber = 2,
                AccessMode = WindowsAccessMode.DriveLetterLegacy,
                DriveLetter = 'Y',
                MountPathUNC = inaccessibleDir
            },
            new()
            {
                Id = "orphan",
                DiskIndex = 43,
                PartitionNumber = 1,
                AccessMode = WindowsAccessMode.DriveLetterLegacy,
                DriveLetter = 'X'
            }
        };

        File.WriteAllText(
            stateFile,
            JsonSerializer.Serialize(mounts, new JsonSerializerOptions { WriteIndented = true }));

        try
        {
            var orphaned = await service.ReconcileMountStateAsync();

            orphaned.Should().ContainSingle(m => m.Id == "orphan");

            var reconciledMounts = await service.GetActiveMountsAsync();

            reconciledMounts.Should().HaveCount(2);

            var verifiedMap = reconciledMounts.ToDictionary(m => m.Id, m => m.IsVerified);

            verifiedMap["mount-1"].Should().BeTrue();
            verifiedMap["mount-2"].Should().BeFalse();
        }
        finally
        {
            service.Dispose();

            if (File.Exists(stateFile))
            {
                File.Delete(stateFile);
            }

            if (Directory.Exists(accessibleDir))
            {
                Directory.Delete(accessibleDir, true);
            }
        }
    }

    [Fact]
    public async Task UnregisterDiskAsync_NonExistentDisk_DoesNotThrow()
    {
        // Act
        Func<Task> act = async () => await _service.UnregisterDiskAsync(999);

        // Assert
        await act.Should().NotThrowAsync();
    }

    private MountStateService CreateService(IDriveLetterService driveLetterService, string? stateFilePath = null)
    {
        var config = new LiMountConfiguration
        {
            History = new HistoryConfig
            {
                StateFilePath = stateFilePath ?? _testStateFilePath
            },
            MountOperations = new MountOperationsConfig
            {
                ReconcileUncAccessibilityTimeoutMs = 200
            }
        };

        var options = Options.Create(config);
        return new MountStateService(
            NullLogger<MountStateService>.Instance,
            driveLetterService,
            options,
            stateFilePath ?? _testStateFilePath);
    }

    private sealed class TestDriveLetterService : IDriveLetterService
    {
        private readonly IReadOnlyCollection<char> _usedLetters;

        public TestDriveLetterService(IEnumerable<char> usedLetters)
        {
            _usedLetters = usedLetters.Select(char.ToUpperInvariant).ToList();
        }

        public IReadOnlyList<char> GetUsedLetters() => _usedLetters.ToList();

        public IReadOnlyList<char> GetFreeLetters()
        {
            var used = new HashSet<char>(_usedLetters);
            return Enumerable.Range('A', 26)
                .Select(i => (char)i)
                .Where(c => !used.Contains(c))
                .OrderByDescending(c => c)
                .ToList();
        }

        public bool IsLetterAvailable(char letter, IReadOnlyCollection<char>? usedLetters = null)
        {
            var used = usedLetters != null
                ? new HashSet<char>(usedLetters.Select(char.ToUpperInvariant))
                : new HashSet<char>(_usedLetters);

            return !used.Contains(char.ToUpperInvariant(letter));
        }
    }
}
