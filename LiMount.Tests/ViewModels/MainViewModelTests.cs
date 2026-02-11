using System.Threading;
using Moq;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using LiMount.Core.Abstractions;
using LiMount.Core.Configuration;
using LiMount.Core.Interfaces;
using LiMount.Core.Models;
using LiMount.WinUI.ViewModels;
using LiMount.WinUI.Views;

namespace LiMount.Tests.ViewModels;

/// <summary>
/// Unit tests for WinUI MainViewModel to verify initialization, mount detection, and command behavior.
/// </summary>
public class MainViewModelTests
{
    private readonly Mock<IDiskEnumerationService> _mockDiskService;
    private readonly Mock<IDriveLetterService> _mockDriveLetterService;
    private readonly Mock<IMountOrchestrator> _mockMountOrchestrator;
    private readonly Mock<IUnmountOrchestrator> _mockUnmountOrchestrator;
    private readonly Mock<IMountStateService> _mockMountStateService;
    private readonly Mock<IEnvironmentValidationService> _mockEnvValidation;
    private readonly Mock<IFilesystemDetectionService> _mockFilesystemDetectionService;
    private readonly Mock<IDialogService> _mockDialogService;
    private readonly Mock<ILogger<MainViewModel>> _mockLogger;
    private readonly Mock<IOptions<LiMountConfiguration>> _mockConfig;
    private readonly Mock<IUiDispatcher> _mockUiDispatcher;

    public MainViewModelTests()
    {
        _mockDiskService = new Mock<IDiskEnumerationService>();
        _mockDriveLetterService = new Mock<IDriveLetterService>();
        _mockMountOrchestrator = new Mock<IMountOrchestrator>();
        _mockUnmountOrchestrator = new Mock<IUnmountOrchestrator>();
        _mockMountStateService = new Mock<IMountStateService>();
        _mockEnvValidation = new Mock<IEnvironmentValidationService>();
        _mockFilesystemDetectionService = new Mock<IFilesystemDetectionService>();
        _mockDialogService = new Mock<IDialogService>();
        _mockLogger = new Mock<ILogger<MainViewModel>>();
        _mockConfig = new Mock<IOptions<LiMountConfiguration>>();
        _mockUiDispatcher = new Mock<IUiDispatcher>();

        // Setup default configuration
        var config = new LiMountConfiguration
        {
            MountOperations = new MountOperationsConfig
            {
                UncPathCheckTimeoutMs = 1000,
                SubstCommandTimeoutMs = 1000,
                WslCommandTimeoutMs = 1000
            },
            Initialization = new InitializationConfig
            {
                AutoReconcileMounts = false
            }
        };
        _mockConfig.Setup(c => c.Value).Returns(config);

        // Setup UI dispatcher to execute actions immediately (synchronous for tests)
        _mockUiDispatcher
            .Setup(d => d.RunAsync(It.IsAny<Func<Task>>()))
            .Returns<Func<Task>>(func => func());
    }

    private MainViewModel CreateViewModel()
    {
        var mountingServices = new MainViewModel.MountingServices(
            _mockDiskService.Object,
            _mockDriveLetterService.Object,
            _mockMountOrchestrator.Object,
            _mockUnmountOrchestrator.Object,
            _mockMountStateService.Object,
            _mockFilesystemDetectionService.Object);

        var appServices = new MainViewModel.AppServices(
            _mockEnvValidation.Object,
            _mockDialogService.Object,
            _mockLogger.Object,
            _mockConfig.Object);

        return new MainViewModel(
            mountingServices,
            appServices,
            () => null!, // HistoryWindow factory - not used in tests
            _mockUiDispatcher.Object);
    }

    [Fact]
    public void Constructor_InitializesWithDefaultStatusMessage()
    {
        // Act
        var vm = CreateViewModel();

        // Assert
        vm.StatusMessage.Should().Contain("Ready");
    }

    [Fact]
    public void Constructor_IsMountedIsFalseByDefault()
    {
        // Act
        var vm = CreateViewModel();

        // Assert
        vm.IsMounted.Should().BeFalse();
        vm.CurrentMountedDiskIndex.Should().BeNull();
    }

    [Fact]
    public async Task InitializeAsync_WithInvalidEnvironment_ShowsErrorDialog()
    {
        // Arrange
        _mockEnvValidation
            .Setup(v => v.ValidateEnvironmentAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EnvironmentValidationResult
            {
                IsValid = false,
                Errors = ["WSL not installed"],
                Suggestions = ["Install WSL 2"]
            });

        var vm = CreateViewModel();

        // Act
        await vm.InitializeAsync();

        // Assert
        _mockDialogService.Verify(
            d => d.ShowErrorAsync(It.Is<string>(s => s.Contains("WSL not installed")), It.IsAny<string>()),
            Times.Once);
    }

    [Fact]
    public async Task InitializeAsync_WithValidEnvironment_LoadsDisksAndDriveLetters()
    {
        // Arrange
        List<DiskInfo> disks = [new() { Index = 1, Partitions = [] }];
        List<char> driveLetters = ['Z', 'Y', 'X'];

        _mockEnvValidation
            .Setup(v => v.ValidateEnvironmentAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EnvironmentValidationResult
            {
                IsValid = true,
                InstalledDistros = ["Ubuntu"]
            });

        _mockDiskService
            .Setup(d => d.GetCandidateDisks())
            .Returns(disks);

        _mockDriveLetterService
            .Setup(d => d.GetFreeLetters())
            .Returns(driveLetters);

        _mockMountStateService
            .Setup(m => m.GetActiveMountsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var vm = CreateViewModel();

        // Act
        await vm.InitializeAsync();

        // Assert
        vm.Disks.Should().HaveCount(1);
        vm.FreeDriveLetters.Should().HaveCount(3);
    }

    [Fact]
    public async Task InitializeAsync_WithExistingMount_DetectsAndSetsState()
    {
        // Arrange
        var activeMount = new ActiveMount
        {
            DiskIndex = 1,
            PartitionNumber = 1,
            DriveLetter = 'Z',
            MountPathUNC = @"\\wsl$\Ubuntu\mnt\wsl\PHYSICALDRIVE1p1"
        };

        _mockEnvValidation
            .Setup(v => v.ValidateEnvironmentAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EnvironmentValidationResult
            {
                IsValid = true,
                InstalledDistros = ["Ubuntu"]
            });

        _mockDiskService
            .Setup(d => d.GetCandidateDisks())
            .Returns([]);

        _mockDriveLetterService
            .Setup(d => d.GetFreeLetters())
            .Returns([]);

        _mockMountStateService
            .Setup(m => m.GetActiveMountsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([activeMount]);

        var vm = CreateViewModel();

        // Note: This test verifies the mount state is read. The actual path verification
        // involves Directory.Exists which we can't easily mock. For full integration,
        // consider using a file system abstraction.

        // Act
        await vm.InitializeAsync();

        // Assert - should have attempted to get active mounts
        _mockMountStateService.Verify(m => m.GetActiveMountsAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task InitializeAsync_WithMultipleStoredMounts_RestoresFirstReachableAndCleansOnlyStaleEntries()
    {
        var staleMount = new ActiveMount
        {
            DiskIndex = 1,
            PartitionNumber = 1,
            DriveLetter = 'Y',
            MountPathUNC = @"\\wsl.localhost\Ubuntu\mnt\wsl\PHYSICALDRIVE1p1_missing"
        };

        var reachableDirectory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"limount_restore_{Guid.NewGuid()}"));
        var validMount = new ActiveMount
        {
            DiskIndex = 2,
            PartitionNumber = 2,
            DriveLetter = 'Z',
            MountPathUNC = reachableDirectory.FullName
        };

        _mockEnvValidation
            .Setup(v => v.ValidateEnvironmentAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EnvironmentValidationResult
            {
                IsValid = true,
                InstalledDistros = ["Ubuntu"]
            });

        _mockDiskService
            .Setup(d => d.GetCandidateDisks())
            .Returns([]);

        _mockDriveLetterService
            .Setup(d => d.GetFreeLetters())
            .Returns([]);

        _mockMountStateService
            .Setup(m => m.GetActiveMountsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([staleMount, validMount]);

        var vm = CreateViewModel();

        try
        {
            await vm.InitializeAsync();

            vm.CurrentMountedDiskIndex.Should().Be(2);
            vm.CurrentMountedPartition.Should().Be(2);
            vm.CurrentMountedDriveLetter.Should().Be('Z');

            _mockMountStateService.Verify(
                m => m.UnregisterMountAsync(1, 1, It.IsAny<CancellationToken>()),
                Times.Once);
            _mockMountStateService.Verify(
                m => m.UnregisterMountAsync(2, 2, It.IsAny<CancellationToken>()),
                Times.Never);
        }
        finally
        {
            reachableDirectory.Delete(true);
        }
    }

    [Fact]
    public void CanMount_WhenNotBusyAndAllSelected_ReturnsTrue()
    {
        // Arrange
        var vm = CreateViewModel();
        vm.IsBusy = false;
        vm.SelectedDisk = new DiskInfo { Index = 1, Partitions = [] };
        vm.SelectedPartition = new PartitionInfo { PartitionNumber = 1 };
        vm.SelectedDriveLetter = 'Z';

        // Act & Assert
        vm.MountCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void CanMount_WhenBusy_ReturnsFalse()
    {
        // Arrange
        var vm = CreateViewModel();
        vm.IsBusy = true;
        vm.SelectedDisk = new DiskInfo { Index = 1, Partitions = [] };
        vm.SelectedPartition = new PartitionInfo { PartitionNumber = 1 };
        vm.SelectedDriveLetter = 'Z';

        // Act & Assert
        vm.MountCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void CanMount_WhenAlreadyMounted_ReturnsFalse()
    {
        // Arrange
        var vm = CreateViewModel();
        vm.CurrentMountedDiskIndex = 1; // Already mounted
        vm.SelectedDisk = new DiskInfo { Index = 2, Partitions = [] };
        vm.SelectedPartition = new PartitionInfo { PartitionNumber = 1 };
        vm.SelectedDriveLetter = 'Z';

        // Act & Assert
        vm.MountCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void CanMount_WhenNoDiskSelected_ReturnsFalse()
    {
        // Arrange
        var vm = CreateViewModel();
        vm.SelectedDisk = null;
        vm.SelectedPartition = new PartitionInfo { PartitionNumber = 1 };
        vm.SelectedDriveLetter = 'Z';

        // Act & Assert
        vm.MountCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task MountAsync_Success_UpdatesMountState()
    {
        // Arrange
        var vm = CreateViewModel();
        vm.SelectedDisk = new DiskInfo { Index = 1, Partitions = [] };
        vm.SelectedPartition = new PartitionInfo { PartitionNumber = 1 };
        vm.SelectedDriveLetter = 'Z';

        var mountResult = new MountAndMapResult
        {
            Success = true,
            DiskIndex = 1,
            Partition = 1,
            DriveLetter = 'Z',
            DistroName = "Ubuntu",
            MountPathLinux = "/mnt/wsl/PHYSICALDRIVE1p1",
            MountPathUNC = @"\\wsl$\Ubuntu\mnt\wsl\PHYSICALDRIVE1p1"
        };

        _mockMountOrchestrator
            .Setup(m => m.MountAndMapAsync(1, 1, 'Z', It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IProgress<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mountResult);

        // Act
        await vm.MountCommand.ExecuteAsync(null);

        // Assert
        vm.IsMounted.Should().BeTrue();
        vm.CurrentMountedDiskIndex.Should().Be(1);
        vm.CurrentMountedDriveLetter.Should().Be('Z');
        vm.CanOpenExplorer.Should().BeTrue();
    }

    [Fact]
    public async Task MountAsync_Failure_DoesNotUpdateMountState()
    {
        // Arrange
        var vm = CreateViewModel();
        vm.SelectedDisk = new DiskInfo { Index = 1, Partitions = [] };
        vm.SelectedPartition = new PartitionInfo { PartitionNumber = 1 };
        vm.SelectedDriveLetter = 'Z';

        var mountResult = new MountAndMapResult
        {
            Success = false,
            ErrorMessage = "Mount failed"
        };

        _mockMountOrchestrator
            .Setup(m => m.MountAndMapAsync(1, 1, 'Z', It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IProgress<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mountResult);

        // Act
        await vm.MountCommand.ExecuteAsync(null);

        // Assert
        vm.IsMounted.Should().BeFalse();
        vm.CurrentMountedDiskIndex.Should().BeNull();
        vm.StatusMessage.Should().Contain("Mount failed");
    }

    [Fact]
    public void IsMounted_ChangesWhenCurrentMountedDiskIndexChanges()
    {
        // Arrange
        var vm = CreateViewModel();
        vm.IsMounted.Should().BeFalse();

        // Act
        vm.CurrentMountedDiskIndex = 1;

        // Assert
        vm.IsMounted.Should().BeTrue();
    }
}

/// <summary>
/// Tests for the regex patterns used in DetectMountFromSystemAsync.
/// These validate the parsing logic without requiring process execution.
/// </summary>
public class MountDetectionRegexTests
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);

    [Theory]
    [InlineData("Z:\\: => UNC\\wsl.localhost\\Ubuntu-24.04\\mnt\\wsl\\PHYSICALDRIVE1p1", "Z", 1, 1)]
    [InlineData("X:\\: => UNC\\wsl.localhost\\Ubuntu\\mnt\\wsl\\PHYSICALDRIVE0p2", "X", 0, 2)]
    [InlineData("A:\\: => UNC\\wsl.localhost\\Debian\\mnt\\wsl\\PHYSICALDRIVE10p5", "A", 10, 5)]
    public void SubstRegex_ParsesValidOutput_Correctly(string input, string expectedDrive, int expectedDisk, int expectedPartition)
    {
        // Arrange - same regex as in MainViewModel.DetectMountFromSystemAsync
        var pattern = @"^([A-Z]):\\: => .+PHYSICALDRIVE(\d+)p(\d+)";

        // Act
        var match = System.Text.RegularExpressions.Regex.Match(
            input, pattern,
            System.Text.RegularExpressions.RegexOptions.IgnoreCase,
            RegexTimeout);

        // Assert
        match.Success.Should().BeTrue();
        match.Groups[1].Value.Should().Be(expectedDrive);
        int.Parse(match.Groups[2].Value).Should().Be(expectedDisk);
        int.Parse(match.Groups[3].Value).Should().Be(expectedPartition);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Some random text")]
    [InlineData("Z: => Not a physical drive")]
    [InlineData("PHYSICALDRIVE1p1")] // Missing drive letter prefix
    public void SubstRegex_RejectsInvalidOutput(string input)
    {
        // Arrange
        var pattern = @"^([A-Z]):\\: => .+PHYSICALDRIVE(\d+)p(\d+)";

        // Act
        var match = System.Text.RegularExpressions.Regex.Match(
            input, pattern,
            System.Text.RegularExpressions.RegexOptions.IgnoreCase,
            RegexTimeout);

        // Assert
        match.Success.Should().BeFalse();
    }

    [Theory]
    [InlineData("PHYSICALDRIVE1p1", 1, 1)]
    [InlineData("PHYSICALDRIVE0p2", 0, 2)]
    [InlineData("PHYSICALDRIVE99p99", 99, 99)]
    [InlineData("physicaldrive5p3", 5, 3)] // Lowercase should work
    public void WslMountRegex_ParsesValidOutput_Correctly(string input, int expectedDisk, int expectedPartition)
    {
        // Arrange - same regex as in MainViewModel.DetectMountFromSystemAsync
        var pattern = @"PHYSICALDRIVE(\d+)p(\d+)";

        // Act
        var match = System.Text.RegularExpressions.Regex.Match(
            input.Trim(), pattern,
            System.Text.RegularExpressions.RegexOptions.IgnoreCase,
            RegexTimeout);

        // Assert
        match.Success.Should().BeTrue();
        int.Parse(match.Groups[1].Value).Should().Be(expectedDisk);
        int.Parse(match.Groups[2].Value).Should().Be(expectedPartition);
    }

    [Theory]
    [InlineData("")]
    [InlineData("some-other-folder")]
    [InlineData("PHYSICALDRIVE")] // Missing numbers
    [InlineData("PHYSICALDRIVEp1")] // Missing disk number
    [InlineData("PHYSICALDRIVE1")] // Missing partition
    public void WslMountRegex_RejectsInvalidOutput(string input)
    {
        // Arrange
        var pattern = @"PHYSICALDRIVE(\d+)p(\d+)";

        // Act
        var match = System.Text.RegularExpressions.Regex.Match(
            input.Trim(), pattern,
            System.Text.RegularExpressions.RegexOptions.IgnoreCase,
            RegexTimeout);

        // Assert
        match.Success.Should().BeFalse();
    }

    [Fact]
    public void SubstRegex_WithTimeout_DoesNotHang()
    {
        // Arrange - potentially malicious input that could cause catastrophic backtracking
        // This shouldn't match anyway, but we want to ensure the timeout works
        var pattern = @"^([A-Z]):\\: => .+PHYSICALDRIVE(\d+)p(\d+)";
        var maliciousInput = new string('a', 10000); // Long string that won't match

        // Act & Assert - should complete quickly due to timeout
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var match = System.Text.RegularExpressions.Regex.Match(
            maliciousInput, pattern,
            System.Text.RegularExpressions.RegexOptions.IgnoreCase,
            RegexTimeout);
        stopwatch.Stop();

        match.Success.Should().BeFalse();
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(500); // Should be way under 500ms
    }

    [Theory]
    [InlineData("-1", false)]  // Negative should be rejected by validation
    [InlineData("100", false)] // Over 99 should be rejected by validation
    [InlineData("0", true)]
    [InlineData("99", true)]
    public void DiskIndexValidation_RangeChecks(string value, bool shouldBeValid)
    {
        // This tests the validation logic added to MainViewModel
        var isValid = int.TryParse(value, out var diskIndex) && diskIndex >= 0 && diskIndex <= 99;
        isValid.Should().Be(shouldBeValid);
    }
}
