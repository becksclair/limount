using FluentAssertions;
using LiMount.Core.ViewModels;
using Xunit;

namespace LiMount.Tests.ViewModels;

public class BaseMainViewModelDetectionTests
{
    [Fact]
    public void ParseMountedPhysicalDriveFromMountOutput_WithRealMountLine_ReturnsDiskAndPartition()
    {
        // Arrange
        const string mountOutput = "/dev/sde2 on /mnt/wsl/PHYSICALDRIVE1p2 type xfs (rw,relatime)";

        // Act
        var parsed = BaseMainViewModel.ParseMountedPhysicalDriveFromMountOutput(mountOutput);

        // Assert
        parsed.Should().Be((1, 2));
    }

    [Fact]
    public void ParseMountedPhysicalDriveFromMountOutput_WithDirectoryListingOnly_ReturnsNull()
    {
        // Arrange
        const string staleListOutput = "PHYSICALDRIVE1p2\nresolv.conf\n";

        // Act
        var parsed = BaseMainViewModel.ParseMountedPhysicalDriveFromMountOutput(staleListOutput);

        // Assert
        parsed.Should().BeNull();
    }

    [Fact]
    public void FindStalePhysicalDriveDirectories_WithMountedAndUnMountedEntries_ReturnsOnlyStale()
    {
        // Arrange
        const string directoryListing = "PHYSICALDRIVE1p2\nPHYSICALDRIVE2p1\nresolv.conf\n";
        const string mountOutput = "/dev/sde2 on /mnt/wsl/PHYSICALDRIVE1p2 type xfs (rw,relatime)";

        // Act
        var stale = BaseMainViewModel.FindStalePhysicalDriveDirectories(directoryListing, mountOutput);

        // Assert
        stale.Should().ContainSingle().Which.Should().Be("PHYSICALDRIVE2p1");
    }

    [Fact]
    public void FindStalePhysicalDriveDirectories_IgnoresInvalidEntries()
    {
        // Arrange
        const string directoryListing = "PHYSICALDRIVE1p2\nPHYSICALDRIVE999p1\nPHYSICALDRIVE2pabc\nnotes.txt\n";
        const string mountOutput = "";

        // Act
        var stale = BaseMainViewModel.FindStalePhysicalDriveDirectories(directoryListing, mountOutput);

        // Assert
        stale.Should().ContainSingle().Which.Should().Be("PHYSICALDRIVE1p2");
    }
}
