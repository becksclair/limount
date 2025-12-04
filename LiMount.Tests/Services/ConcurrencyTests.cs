using FluentAssertions;
using Microsoft.Extensions.Options;
using LiMount.Core.Configuration;
using LiMount.Core.Models;
using LiMount.Core.Services;

namespace LiMount.Tests.Services;

/// <summary>
/// Tests for concurrent access patterns and thread safety.
/// Verifies that services handle simultaneous operations correctly.
/// </summary>
public class ConcurrencyTests : IDisposable
{
    private readonly string _testHistoryFilePath;
    private readonly string _testStateFilePath;
    private readonly IOptions<LiMountConfiguration> _config;

    public ConcurrencyTests()
    {
        _testHistoryFilePath = Path.Combine(Path.GetTempPath(), $"limount_concurrent_history_{Guid.NewGuid()}.json");
        _testStateFilePath = Path.Combine(Path.GetTempPath(), $"limount_concurrent_state_{Guid.NewGuid()}.json");
        _config = Options.Create(new LiMountConfiguration());
    }

    public void Dispose()
    {
        if (File.Exists(_testHistoryFilePath))
            File.Delete(_testHistoryFilePath);
        if (File.Exists(_testStateFilePath))
            File.Delete(_testStateFilePath);
    }

    #region MountHistoryService Concurrency Tests

    [Fact]
    public async Task MountHistoryService_ConcurrentAddEntries_AllEntriesPersistedCorrectly()
    {
        // Arrange
        var service = new MountHistoryService(NullLogger<MountHistoryService>.Instance, _config, _testHistoryFilePath);
        const int concurrentCount = 20;
        var entries = Enumerable.Range(0, concurrentCount)
            .Select(i => new MountHistoryEntry
            {
                Id = Guid.NewGuid().ToString(),
                Timestamp = DateTime.Now.AddSeconds(i),
                OperationType = MountHistoryOperationType.Mount,
                DiskIndex = i,
                PartitionNumber = 1,
                DriveLetter = (char)('A' + (i % 26)),
                Success = true
            })
            .ToList();

        // Act - add entries concurrently
        var tasks = entries.Select(e => service.AddEntryAsync(e)).ToList();
        await Task.WhenAll(tasks);

        // Assert - all entries should be persisted
        var history = await service.GetHistoryAsync();
        history.Should().HaveCount(concurrentCount);
        history.Select(h => h.Id).Distinct().Should().HaveCount(concurrentCount);
    }

    [Fact]
    public async Task MountHistoryService_ConcurrentReadAndWrite_NoCorruptionOrExceptions()
    {
        // Arrange
        var service = new MountHistoryService(NullLogger<MountHistoryService>.Instance, _config, _testHistoryFilePath);
        const int iterations = 50;
        var exceptions = new List<Exception>();

        // Act - concurrent reads and writes
        var writeTasks = Enumerable.Range(0, iterations / 2).Select(async i =>
        {
            try
            {
                await service.AddEntryAsync(new MountHistoryEntry
                {
                    Id = Guid.NewGuid().ToString(),
                    Timestamp = DateTime.Now,
                    OperationType = i % 2 == 0 ? MountHistoryOperationType.Mount : MountHistoryOperationType.Unmount,
                    DiskIndex = i,
                    PartitionNumber = 1,
                    Success = true
                });
            }
            catch (Exception ex)
            {
                lock (exceptions) { exceptions.Add(ex); }
            }
        });

        var readTasks = Enumerable.Range(0, iterations / 2).Select(async _ =>
        {
            try
            {
                await service.GetHistoryAsync();
            }
            catch (Exception ex)
            {
                lock (exceptions) { exceptions.Add(ex); }
            }
        });

        await Task.WhenAll(writeTasks.Concat(readTasks));

        // Assert - no exceptions during concurrent operations
        exceptions.Should().BeEmpty("concurrent read/write operations should not throw exceptions");

        // Verify data integrity
        var finalHistory = await service.GetHistoryAsync();
        finalHistory.Should().NotBeNull();
        finalHistory.All(e => !string.IsNullOrEmpty(e.Id)).Should().BeTrue();
    }

    #endregion

    #region MountStateService Concurrency Tests

    [Fact]
    public async Task MountStateService_ConcurrentRegisterAndGet_NoCorruption()
    {
        // Arrange
        var driveService = new TestDriveLetterService(Enumerable.Range('A', 26).Select(i => (char)i).Take(10));
        using var service = new MountStateService(NullLogger<MountStateService>.Instance, driveService, _config, _testStateFilePath);
        const int concurrentCount = 10;
        var exceptions = new List<Exception>();

        // Act - concurrent registrations
        var registerTasks = Enumerable.Range(0, concurrentCount).Select(async i =>
        {
            try
            {
                var mount = new ActiveMount
                {
                    Id = Guid.NewGuid().ToString(),
                    MountedAt = DateTime.Now,
                    DiskIndex = i,
                    PartitionNumber = 1,
                    DriveLetter = (char)('A' + i),
                    DistroName = "Ubuntu",
                    MountPathLinux = $"/mnt/wsl/PHYSICALDRIVE{i}p1",
                    MountPathUNC = $@"\\wsl$\Ubuntu\mnt\wsl\PHYSICALDRIVE{i}p1",
                    IsVerified = true
                };
                await service.RegisterMountAsync(mount);
            }
            catch (Exception ex)
            {
                lock (exceptions) { exceptions.Add(ex); }
            }
        });

        var readTasks = Enumerable.Range(0, concurrentCount).Select(async i =>
        {
            try
            {
                await service.GetMountForDiskAsync(i);
            }
            catch (Exception ex)
            {
                lock (exceptions) { exceptions.Add(ex); }
            }
        });

        await Task.WhenAll(registerTasks.Concat(readTasks));

        // Assert
        exceptions.Should().BeEmpty("concurrent operations should not throw");
    }

    [Fact]
    public async Task MountStateService_RapidRegisterUnregister_HandlesCorrectly()
    {
        // Arrange
        var driveService = new TestDriveLetterService(new[] { 'Z', 'Y', 'X' });
        using var service = new MountStateService(NullLogger<MountStateService>.Instance, driveService, _config, _testStateFilePath);
        const int iterations = 20;

        // Act - rapid register/unregister cycles
        for (int i = 0; i < iterations; i++)
        {
            var mount = new ActiveMount
            {
                Id = Guid.NewGuid().ToString(),
                MountedAt = DateTime.Now,
                DiskIndex = 1,
                PartitionNumber = 1,
                DriveLetter = 'Z',
                DistroName = "Ubuntu",
                IsVerified = true
            };

            await service.RegisterMountAsync(mount);
            await service.UnregisterMountAsync(1);
        }

        // Assert - should end with empty state
        var activeMounts = await service.GetActiveMountsAsync();
        activeMounts.Should().BeEmpty();
    }

    [Fact]
    public async Task MountStateService_ConcurrentReconciliation_NoDeadlock()
    {
        // Arrange
        var driveService = new TestDriveLetterService(new[] { 'Z', 'Y' });
        using var service = new MountStateService(NullLogger<MountStateService>.Instance, driveService, _config, _testStateFilePath);

        // Add initial mounts
        await service.RegisterMountAsync(new ActiveMount
        {
            Id = Guid.NewGuid().ToString(),
            DiskIndex = 1,
            DriveLetter = 'Z',
            DistroName = "Ubuntu"
        });

        // Act - concurrent reconciliations
        var tasks = Enumerable.Range(0, 5).Select(_ => service.ReconcileMountStateAsync()).ToList();

        // Should complete without deadlock
        var timeout = Task.Delay(TimeSpan.FromSeconds(10));
        var completed = await Task.WhenAny(Task.WhenAll(tasks), timeout);

        // Assert
        completed.Should().NotBe(timeout, "reconciliation should complete without deadlock");
    }

    #endregion

    #region Cross-Service Concurrency Tests

    [Fact]
    public async Task MultipleServices_ConcurrentAccess_NoInterference()
    {
        // Arrange
        var historyPath1 = Path.Combine(Path.GetTempPath(), $"limount_multi_1_{Guid.NewGuid()}.json");
        var historyPath2 = Path.Combine(Path.GetTempPath(), $"limount_multi_2_{Guid.NewGuid()}.json");
        var service1 = new MountHistoryService(NullLogger<MountHistoryService>.Instance, _config, historyPath1);
        var service2 = new MountHistoryService(NullLogger<MountHistoryService>.Instance, _config, historyPath2);

        try
        {
            // Act - both services working concurrently
            var tasks = new List<Task>();

            for (int i = 0; i < 10; i++)
            {
                tasks.Add(service1.AddEntryAsync(new MountHistoryEntry
                {
                    Id = $"s1-{i}",
                    Timestamp = DateTime.Now,
                    OperationType = MountHistoryOperationType.Mount,
                    DiskIndex = i
                }));

                tasks.Add(service2.AddEntryAsync(new MountHistoryEntry
                {
                    Id = $"s2-{i}",
                    Timestamp = DateTime.Now,
                    OperationType = MountHistoryOperationType.Unmount,
                    DiskIndex = i
                }));
            }

            await Task.WhenAll(tasks);

            // Assert - each service has its own isolated data
            var history1 = await service1.GetHistoryAsync();
            var history2 = await service2.GetHistoryAsync();

            history1.Should().HaveCount(10);
            history1.All(h => h.Id.StartsWith("s1-")).Should().BeTrue();

            history2.Should().HaveCount(10);
            history2.All(h => h.Id.StartsWith("s2-")).Should().BeTrue();
        }
        finally
        {
            if (File.Exists(historyPath1)) File.Delete(historyPath1);
            if (File.Exists(historyPath2)) File.Delete(historyPath2);
        }
    }

    #endregion
}
