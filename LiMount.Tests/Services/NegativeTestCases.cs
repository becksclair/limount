using FluentAssertions;
using Microsoft.Extensions.Options;
using LiMount.Core.Configuration;
using LiMount.Core.Interfaces;
using LiMount.Core.Models;
using LiMount.Core.Results;
using LiMount.Core.Services;

namespace LiMount.Tests.Services;

/// <summary>
/// Negative test cases and edge case handling tests.
/// Tests error conditions, boundary values, and invalid inputs.
/// </summary>
public class NegativeTestCases : IDisposable
{
    private readonly string _testHistoryFilePath;
    private readonly string _testStateFilePath;
    private readonly IOptions<LiMountConfiguration> _config;

    public NegativeTestCases()
    {
        _testHistoryFilePath = Path.Combine(Path.GetTempPath(), $"limount_test_history_{Guid.NewGuid()}.json");
        _testStateFilePath = Path.Combine(Path.GetTempPath(), $"limount_test_state_{Guid.NewGuid()}.json");
        _config = Options.Create(new LiMountConfiguration());
    }

    public void Dispose()
    {
        if (File.Exists(_testHistoryFilePath))
            File.Delete(_testHistoryFilePath);
        if (File.Exists(_testStateFilePath))
            File.Delete(_testStateFilePath);
    }

    #region MountHistoryService Negative Tests

    [Fact]
    public void MountHistoryService_NullLogger_ThrowsArgumentNullException()
    {
        // Act & Assert
        var act = () => new MountHistoryService(null!, _config, _testHistoryFilePath);
        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public async Task MountHistoryService_EmptyHistory_ReturnsEmptyList()
    {
        // Arrange
        var service = new MountHistoryService(NullLogger<MountHistoryService>.Instance, _config, _testHistoryFilePath);

        // Act
        var history = await service.GetHistoryAsync();

        // Assert
        history.Should().NotBeNull();
        history.Should().BeEmpty();
    }

    [Fact]
    public async Task MountHistoryService_GetHistory_WithEmptyFile_ReturnsEmptyList()
    {
        // Arrange
        var service = new MountHistoryService(NullLogger<MountHistoryService>.Instance, _config, _testHistoryFilePath);

        // Act
        var history = await service.GetHistoryAsync();

        // Assert
        history.Should().NotBeNull();
        history.Should().BeEmpty();
    }

    [Fact]
    public async Task MountHistoryService_AddEntry_WithNullEntry_HandlesGracefully()
    {
        // Arrange
        var service = new MountHistoryService(NullLogger<MountHistoryService>.Instance, _config, _testHistoryFilePath);

        // Act
        var act = async () => await service.AddEntryAsync(null!);

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    #endregion

    #region MountStateService Negative Tests

    [Fact]
    public void MountStateService_NullLogger_ThrowsArgumentNullException()
    {
        // Arrange
        var driveService = new TestDriveLetterService(new[] { 'Z' });

        // Act & Assert
        var act = () => new MountStateService(null!, driveService, _config, _testStateFilePath);
        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public void MountStateService_NullDriveLetterService_ThrowsArgumentNullException()
    {
        // Act & Assert
        var act = () => new MountStateService(NullLogger<MountStateService>.Instance, null!, _config, _testStateFilePath);
        act.Should().Throw<ArgumentNullException>().WithParameterName("driveLetterService");
    }

    [Fact]
    public async Task MountStateService_RegisterMount_WithNullMount_ThrowsArgumentNullException()
    {
        // Arrange
        var driveService = new TestDriveLetterService(new[] { 'Z' });
        using var service = new MountStateService(NullLogger<MountStateService>.Instance, driveService, _config, _testStateFilePath);

        // Act
        var act = async () => await service.RegisterMountAsync(null!);

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-100)]
    public async Task MountStateService_GetMountForDisk_NegativeDiskIndex_ReturnsNull(int diskIndex)
    {
        // Arrange
        var driveService = new TestDriveLetterService(new[] { 'Z' });
        using var service = new MountStateService(NullLogger<MountStateService>.Instance, driveService, _config, _testStateFilePath);

        // Act
        var mount = await service.GetMountForDiskAsync(diskIndex);

        // Assert
        mount.Should().BeNull();
    }

    [Fact]
    public async Task MountStateService_Disposed_ThrowsObjectDisposedException()
    {
        // Arrange
        var driveService = new TestDriveLetterService(new[] { 'Z' });
        var service = new MountStateService(NullLogger<MountStateService>.Instance, driveService, _config, _testStateFilePath);
        service.Dispose();

        // Act & Assert
        await Assert.ThrowsAsync<ObjectDisposedException>(() => service.GetActiveMountsAsync());
    }

    #endregion

    #region DriveLetterService Negative Tests

    [Fact]
    public void DriveLetterService_GetFreeLetters_ReturnsNonNull()
    {
        // Arrange
        var service = new DriveLetterService(NullLogger<DriveLetterService>.Instance);

        // Act
        var letters = service.GetFreeLetters();

        // Assert
        letters.Should().NotBeNull();
    }

    [Fact]
    public void DriveLetterService_GetUsedLetters_ReturnsNonNull()
    {
        // Arrange
        var service = new DriveLetterService(NullLogger<DriveLetterService>.Instance);

        // Act
        var letters = service.GetUsedLetters();

        // Assert
        letters.Should().NotBeNull();
    }

    #endregion

    #region MountOrchestrator Negative Tests

    [Fact]
    public void MountOrchestrator_NullMountScriptService_ThrowsArgumentNullException()
    {
        // Arrange
        var windowsAccessService = new TestWindowsAccessService();

        // Act & Assert
        var act = () => new MountOrchestrator(null!, windowsAccessService, _config);
        act.Should().Throw<ArgumentNullException>().WithParameterName("mountScriptService");
    }

    [Fact]
    public void MountOrchestrator_NullWindowsAccessService_ThrowsArgumentNullException()
    {
        // Arrange
        var mockMountScriptService = new TestMountScriptService();

        // Act & Assert
        var act = () => new MountOrchestrator(mockMountScriptService, null!, _config);
        act.Should().Throw<ArgumentNullException>().WithParameterName("windowsAccessService");
    }

    [Fact]
    public void MountOrchestrator_NullConfig_ThrowsArgumentNullException()
    {
        // Arrange - use mock services
        var mockMountScriptService = new TestMountScriptService();
        var windowsAccessService = new TestWindowsAccessService();

        // Act & Assert
        var act = () => new MountOrchestrator(mockMountScriptService, windowsAccessService, null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("config");
    }

    #endregion

    #region ActiveMount Model Edge Cases

    [Fact]
    public void ActiveMount_DefaultValues_AreValid()
    {
        // Arrange & Act
        var mount = new ActiveMount();

        // Assert - ActiveMount generates a GUID by default
        mount.Id.Should().NotBeNullOrEmpty();
        mount.DiskIndex.Should().Be(0);
        mount.PartitionNumber.Should().Be(0);
        mount.DriveLetter.Should().BeNull();
        mount.IsVerified.Should().BeFalse();
    }

    [Fact]
    public void MountHistoryEntry_DefaultValues_AreValid()
    {
        // Arrange & Act
        var entry = new MountHistoryEntry();

        // Assert
        entry.Id.Should().NotBeEmpty(); // Has default GUID
        entry.OperationType.Should().Be(MountHistoryOperationType.Mount); // default enum value
        entry.DiskIndex.Should().Be(0);
    }

    #endregion

    #region Helper Classes

    /// <summary>
    /// Test implementation of IMountScriptService for testing orchestrators.
    /// </summary>
    private class TestMountScriptService : Core.Interfaces.IMountScriptService
    {
        public Task<MountResult> ExecuteMountScriptAsync(int diskIndex, int partition, string fsType, string? distroName = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new MountResult { Success = false });

        public Task<UnmountResult> ExecuteUnmountScriptAsync(int diskIndex, CancellationToken cancellationToken = default)
            => Task.FromResult(new UnmountResult { Success = false });
    }

    /// <summary>
    /// Test implementation of IWindowsAccessService for testing orchestrators.
    /// </summary>
    private class TestWindowsAccessService : IWindowsAccessService
    {
        public Task<Result<WindowsAccessInfo>> CreateAccessAsync(WindowsAccessRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(Result<WindowsAccessInfo>.Failure("Access creation failed"));

        public Task<Result> RemoveAccessAsync(WindowsAccessInfo accessInfo, CancellationToken cancellationToken = default)
            => Task.FromResult(Result.Failure("Access removal failed"));
    }

    /// <summary>
    /// Test implementation of IFilesystemDetectionService for testing.
    /// </summary>
    private class TestFilesystemDetectionService : Core.Interfaces.IFilesystemDetectionService
    {
        public Task<string?> DetectFilesystemTypeAsync(int diskIndex, int partitionNumber, CancellationToken cancellationToken = default)
            => Task.FromResult<string?>(null);
    }

    #endregion
}

/// <summary>
/// Test implementation of IDriveLetterService for controlled testing.
/// </summary>
public class TestDriveLetterService : Core.Interfaces.IDriveLetterService
{
    private readonly IReadOnlyList<char> _usedLetters;

    public TestDriveLetterService(IEnumerable<char> usedLetters)
    {
        _usedLetters = usedLetters.ToList().AsReadOnly();
    }

    public IReadOnlyList<char> GetUsedLetters() => _usedLetters;

    public IReadOnlyList<char> GetFreeLetters()
    {
        var allLetters = Enumerable.Range('A', 26).Select(i => (char)i);
        return allLetters.Where(l => !_usedLetters.Contains(l)).ToList().AsReadOnly();
    }

    public bool IsLetterAvailable(char letter, IReadOnlyCollection<char>? usedLetters = null)
    {
        var letters = usedLetters ?? _usedLetters;
        return !letters.Contains(char.ToUpperInvariant(letter));
    }
}
