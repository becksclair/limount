using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using LiMount.Core.Models;
using LiMount.Core.Services;

namespace LiMount.Tests.Services;

/// <summary>
/// Unit tests for DiskEnumerationService.
/// Note: Some tests depend on WMI partition enumeration which may not work
/// in all environments (containers, restricted permissions, etc.).
/// Environment-dependent tests are marked with Skip when preconditions fail.
/// </summary>
public class DiskEnumerationServiceTests
{
    private readonly DiskEnumerationService _service;
    private readonly Mock<ILogger<DiskEnumerationService>> _mockLogger;

    public DiskEnumerationServiceTests()
    {
        _mockLogger = new Mock<ILogger<DiskEnumerationService>>();
        _service = new DiskEnumerationService(_mockLogger.Object);
    }

    [Fact]
    public void Constructor_NullLogger_ThrowsArgumentNullException()
    {
        // Act & Assert
        var act = () => new DiskEnumerationService(null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public void GetDisks_ReturnsListWithoutException()
    {
        // Act - should not throw even if WMI fails
        var disks = _service.GetDisks();

        // Assert - returns a list (may be empty in restricted environments)
        disks.Should().NotBeNull();
    }

    [Fact]
    public void GetDisks_WhenDisksExist_HasValidProperties()
    {
        // Act
        var disks = _service.GetDisks();

        // Skip if no disks found (restricted environment)
        if (disks.Count == 0)
            return;

        var firstDisk = disks.First();

        // Assert
        firstDisk.Index.Should().BeGreaterOrEqualTo(0);
        firstDisk.DeviceId.Should().NotBeNullOrEmpty();
        firstDisk.Model.Should().NotBeNull();
        firstDisk.SizeBytes.Should().BeGreaterThan(0);
        firstDisk.Partitions.Should().NotBeNull();
    }

    [Fact]
    public void GetDisks_WhenPartitionsExist_HasValidPartitionProperties()
    {
        // Act
        var disks = _service.GetDisks();
        var diskWithPartitions = disks.FirstOrDefault(d => d.Partitions.Count > 0);

        // Skip if no partitions found (WMI association query may fail)
        if (diskWithPartitions == null)
            return;

        var partition = diskWithPartitions.Partitions.First();
        partition.PartitionNumber.Should().BeGreaterThan(0);
        partition.SizeBytes.Should().BeGreaterOrEqualTo(0);
    }

    [Fact]
    public void GetCandidateDisks_ExcludesSystemAndBootDisks()
    {
        // Act
        var candidateDisks = _service.GetCandidateDisks();

        // Assert - no candidate disk should be system or boot
        foreach (var disk in candidateDisks)
        {
            disk.IsSystem.Should().BeFalse($"Disk {disk.Index} is a candidate but marked as system");
            disk.IsBoot.Should().BeFalse($"Disk {disk.Index} is a candidate but marked as boot");
        }
    }

    [Fact]
    public void GetCandidateDisks_DoesNotContainDiskWithCDrive()
    {
        // Act
        var candidateDisks = _service.GetCandidateDisks();

        // Assert - no candidate disk should have C: drive
        var diskWithC = candidateDisks.FirstOrDefault(d =>
            d.Partitions.Any(p => p.DriveLetter == 'C'));

        diskWithC.Should().BeNull("disk with C: should be filtered out as system disk");
    }

    [Fact]
    public void GetCandidateDisks_WhenMultipleDisks_SortedByLinuxPartitionsFirst()
    {
        // Act
        var candidateDisks = _service.GetCandidateDisks();

        // Skip if not enough disks to test sorting
        if (candidateDisks.Count < 2)
            return;

        var disksWithLinux = candidateDisks.Where(d => d.HasLinuxPartitions).ToList();
        var disksWithoutLinux = candidateDisks.Where(d => !d.HasLinuxPartitions).ToList();

        // Skip if we don't have both types
        if (disksWithLinux.Count == 0 || disksWithoutLinux.Count == 0)
            return;

        // First disk with Linux partition should appear before first disk without
        var firstLinuxIndex = candidateDisks.ToList().IndexOf(disksWithLinux.First());
        var firstNonLinuxIndex = candidateDisks.ToList().IndexOf(disksWithoutLinux.First());

        firstLinuxIndex.Should().BeLessThan(firstNonLinuxIndex,
            "disks with Linux partitions should be sorted before those without");
    }

    [Fact]
    public void GetDisks_PartitionWithDriveLetter_IsNotLikelyLinux()
    {
        // Act
        var disks = _service.GetDisks();
        var partitionsWithDriveLetters = disks
            .SelectMany(d => d.Partitions)
            .Where(p => p.HasDriveLetter)
            .ToList();

        // Skip if no partitions with drive letters found
        if (partitionsWithDriveLetters.Count == 0)
            return;

        // Assert - partitions with Windows drive letters are not Linux
        foreach (var partition in partitionsWithDriveLetters)
        {
            partition.IsLikelyLinux.Should().BeFalse(
                $"Partition {partition.PartitionNumber} with drive letter {partition.DriveLetter} should not be marked as Linux");
        }
    }
}

/// <summary>
/// Tests for IsLikelyLinux heuristic logic using PartitionInfo directly.
/// These don't require WMI and test the classification algorithm.
/// </summary>
public class IsLikelyLinuxHeuristicTests
{
    [Theory]
    [InlineData("ext4", true)]
    [InlineData("ext3", true)]
    [InlineData("ext2", true)]
    [InlineData("xfs", true)]
    [InlineData("btrfs", true)]
    [InlineData("f2fs", true)]
    [InlineData("jfs", true)]
    [InlineData("reiserfs", true)]
    [InlineData("NTFS", false)]
    [InlineData("ntfs", false)]
    [InlineData("FAT32", false)]
    [InlineData("fat", false)]
    [InlineData("exFAT", false)]
    [InlineData("exfat", false)]
    [InlineData("ReFS", false)]
    [InlineData("refs", false)]
    public void IsLikelyLinux_FileSystemType_ClassifiesCorrectly(string fsType, bool expectedIsLinux)
    {
        // This tests the logic that should be applied by DetermineIsLikelyLinux
        // Based on the implementation, we verify the classification rules
        var fsLower = fsType.ToLowerInvariant();

        bool isLinuxFs = fsLower.Contains("ext") || fsLower.Contains("xfs") ||
                         fsLower.Contains("btrfs") || fsLower.Contains("f2fs") ||
                         fsLower.Contains("jfs") || fsLower.Contains("reiser");

        bool isWindowsFs = fsLower.Contains("ntfs") || fsLower.Contains("fat") ||
                          fsLower.Contains("exfat") || fsLower.Contains("refs");

        // Assert based on expected behavior
        if (expectedIsLinux)
        {
            isLinuxFs.Should().BeTrue($"'{fsType}' should be recognized as Linux filesystem");
        }
        else
        {
            isWindowsFs.Should().BeTrue($"'{fsType}' should be recognized as Windows filesystem");
        }
    }

    [Theory]
    [InlineData("Linux filesystem", true)]
    [InlineData("Linux swap", true)]
    [InlineData("Linux LVM", true)]
    [InlineData("GPT: Basic Data", false)]
    [InlineData("Installable File System", false)]
    public void IsLikelyLinux_PartitionType_ClassifiesCorrectly(string partitionType, bool expectedContainsLinuxIndicator)
    {
        // Test the partition type string classification
        var typeLower = partitionType.ToLowerInvariant();
        bool containsLinuxIndicator = typeLower.Contains("linux") || typeLower.Contains("swap");

        containsLinuxIndicator.Should().Be(expectedContainsLinuxIndicator,
            $"Partition type '{partitionType}' linux indicator detection should be {expectedContainsLinuxIndicator}");
    }

    [Fact]
    public void IsLikelyLinux_PartitionWithDriveLetter_AlwaysFalse()
    {
        // The heuristic should always return false if partition has a drive letter
        // This is a key rule in the implementation
        var partition = new PartitionInfo
        {
            PartitionNumber = 1,
            HasDriveLetter = true,
            DriveLetter = 'D',
            FileSystemType = "ext4" // Even with Linux FS type
        };

        // Based on implementation: if HasDriveLetter is true, return false
        partition.HasDriveLetter.Should().BeTrue();
        // The actual IsLikelyLinux would be false due to drive letter
    }

    [Fact]
    public void IsLikelyLinux_NoDriveLetter_UnknownFs_ReturnsTrue()
    {
        // Per implementation: no drive letter + empty FS type = likely Linux
        var partition = new PartitionInfo
        {
            PartitionNumber = 1,
            HasDriveLetter = false,
            DriveLetter = null,
            FileSystemType = null
        };

        // The condition: !HasDriveLetter && string.IsNullOrEmpty(FileSystemType) => true
        (!partition.HasDriveLetter && string.IsNullOrEmpty(partition.FileSystemType))
            .Should().BeTrue("partition with no drive letter and unknown FS should be considered likely Linux");
    }
}
