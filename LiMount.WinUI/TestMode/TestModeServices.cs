using LiMount.Core.Interfaces;
using LiMount.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace LiMount.WinUI.TestMode;

internal static class TestModeServices
{
    internal static IServiceCollection AddLiMountTestModeServices(this IServiceCollection services)
    {
        services.AddSingleton<IDiskEnumerationService, TestDiskEnumerationService>();
        services.AddSingleton<IDriveLetterService, TestDriveLetterService>();
        services.AddSingleton<IMountHistoryService, TestMountHistoryService>();
        services.AddSingleton<IMountStateService, InMemoryMountStateService>();
        services.AddSingleton<IEnvironmentValidationService, TestEnvironmentValidationService>();
        services.AddSingleton<IFilesystemDetectionService, TestFilesystemDetectionService>();
        services.AddSingleton<IMountOrchestrator, TestMountOrchestrator>();
        services.AddSingleton<IUnmountOrchestrator, TestUnmountOrchestrator>();
        return services;
    }
}

internal sealed class TestDiskEnumerationService : IDiskEnumerationService
{
    private readonly IReadOnlyList<DiskInfo> _disks =
    [
        new()
        {
            Index = 1,
            DeviceId = @"\\.\PHYSICALDRIVE1",
            Model = "Test XFS Disk",
            SizeBytes = 2_000_000_000_000,
            Partitions =
            [
                new PartitionInfo
                {
                    PartitionNumber = 1,
                    SizeBytes = 4_000_000_000,
                    Label = "EFI",
                    FileSystemType = "vfat",
                    IsLikelyLinux = false
                },
                new PartitionInfo
                {
                    PartitionNumber = 2,
                    SizeBytes = 1_850_000_000_000,
                    Label = "root",
                    FileSystemType = "xfs",
                    IsLikelyLinux = true
                }
            ]
        },
        new()
        {
            Index = 2,
            DeviceId = @"\\.\PHYSICALDRIVE2",
            Model = "Test EXT4 Disk",
            SizeBytes = 500_000_000_000,
            Partitions =
            [
                new PartitionInfo
                {
                    PartitionNumber = 1,
                    SizeBytes = 500_000_000_000,
                    Label = "data",
                    FileSystemType = "ext4",
                    IsLikelyLinux = true
                }
            ]
        }
    ];

    public IReadOnlyList<DiskInfo> GetDisks() => _disks;

    public IReadOnlyList<DiskInfo> GetCandidateDisks() => _disks;
}

internal sealed class TestDriveLetterService : IDriveLetterService
{
    private static readonly IReadOnlyList<char> UsedLetters = ['C', 'D'];
    private static readonly IReadOnlyList<char> FreeLetters = ['Z', 'Y', 'X', 'W'];

    public IReadOnlyList<char> GetUsedLetters() => UsedLetters;

    public IReadOnlyList<char> GetFreeLetters() => FreeLetters;

    public bool IsLetterAvailable(char letter, IReadOnlyCollection<char>? usedLetters = null)
    {
        var used = usedLetters ?? UsedLetters;
        return !used.Contains(char.ToUpperInvariant(letter));
    }
}

internal sealed class TestEnvironmentValidationService : IEnvironmentValidationService
{
    public Task<EnvironmentValidationResult> ValidateEnvironmentAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new EnvironmentValidationResult
        {
            IsValid = true,
            IsWslInstalled = true,
            HasWslDistro = true,
            InstalledDistros = ["TestDistro"],
            IsWindowsVersionCompatible = true,
            IsWindowsBuildCompatible = true,
            WindowsVersion = new Version(10, 0, 26200, 0),
            WindowsBuildNumber = 26200
        });
    }

    public Task<bool> IsWslInstalledAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);

    public Task<List<string>> GetInstalledDistrosAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new List<string> { "TestDistro" });

    public bool IsWindowsVersionCompatible() => true;

    public Version? GetWindowsVersion() => new Version(10, 0, 26200, 0);

    public int GetWindowsBuildNumber() => 26200;
}

internal sealed class TestFilesystemDetectionService : IFilesystemDetectionService
{
    public Task<string?> DetectFilesystemTypeAsync(int diskIndex, int partitionNumber, CancellationToken cancellationToken = default)
    {
        if (diskIndex == 1 && partitionNumber == 2)
        {
            return Task.FromResult<string?>("xfs");
        }

        return Task.FromResult<string?>("ext4");
    }
}

internal sealed class TestMountOrchestrator : IMountOrchestrator
{
    private readonly string _scenario;

    public TestMountOrchestrator()
    {
        _scenario = (Environment.GetEnvironmentVariable("LIMOUNT_TEST_SCENARIO") ?? "xfs_unsupported")
            .Trim()
            .ToLowerInvariant();
    }

    public async Task<MountAndMapResult> MountAndMapAsync(
        int diskIndex,
        int partition,
        char driveLetter,
        string fsType = "ext4",
        string? distroName = null,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        progress?.Report($"Test mode mount request: disk {diskIndex} partition {partition} as {driveLetter}:");
        await Task.Delay(100, cancellationToken);

        if (_scenario == "xfs_unsupported" && diskIndex == 1 && partition == 2)
        {
            return MountAndMapResult.CreateFailure(
                diskIndex,
                partition,
                "This XFS filesystem uses features unsupported by the current WSL kernel. Update WSL kernel or mount on native Linux. Details: wsl --mount failed (exit code 1): Invalid argument. ErrorCode=XFS_UNSUPPORTED_FEATURES.",
                "mount");
        }

        return MountAndMapResult.CreateSuccess(
            diskIndex,
            partition,
            driveLetter,
            "TestDistro",
            $"/mnt/wsl/PHYSICALDRIVE{diskIndex}p{partition}",
            $@"\\wsl.localhost\TestDistro\mnt\wsl\PHYSICALDRIVE{diskIndex}p{partition}");
    }
}

internal sealed class TestUnmountOrchestrator : IUnmountOrchestrator
{
    public Task<UnmountAndUnmapResult> UnmountAndUnmapAsync(
        int diskIndex,
        char? driveLetter = null,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        progress?.Report($"Test mode unmount request for disk {diskIndex}.");
        return Task.FromResult(UnmountAndUnmapResult.CreateSuccess(
            diskIndex,
            driveLetter));
    }
}

internal sealed class InMemoryMountStateService : IMountStateService
{
    private readonly List<ActiveMount> _mounts = [];
    private readonly object _sync = new();

    public Task<IReadOnlyList<ActiveMount>> GetActiveMountsAsync(CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            return Task.FromResult<IReadOnlyList<ActiveMount>>(_mounts.Select(Clone).ToList().AsReadOnly());
        }
    }

    public Task RegisterMountAsync(ActiveMount mount, CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            _mounts.RemoveAll(m => m.DiskIndex == mount.DiskIndex && m.PartitionNumber == mount.PartitionNumber);
            _mounts.Add(Clone(mount));
        }
        return Task.CompletedTask;
    }

    [Obsolete("Use UnregisterDiskAsync for disk-wide removal or UnregisterMountAsync(diskIndex, partition) for partition-scoped removal.")]
    public Task UnregisterMountAsync(int diskIndex, CancellationToken cancellationToken = default)
    {
        return UnregisterDiskAsync(diskIndex, cancellationToken);
    }

    public Task UnregisterMountAsync(int diskIndex, int partition, CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            _mounts.RemoveAll(m => m.DiskIndex == diskIndex && m.PartitionNumber == partition);
        }
        return Task.CompletedTask;
    }

    public Task UnregisterDiskAsync(int diskIndex, CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            _mounts.RemoveAll(m => m.DiskIndex == diskIndex);
        }
        return Task.CompletedTask;
    }

    public Task<ActiveMount?> GetMountForDiskAsync(int diskIndex, CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            return Task.FromResult(_mounts.FirstOrDefault(m => m.DiskIndex == diskIndex)?.Let(Clone));
        }
    }

    public Task<ActiveMount?> GetMountForDiskPartitionAsync(int diskIndex, int partition, CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            return Task.FromResult(_mounts.FirstOrDefault(m => m.DiskIndex == diskIndex && m.PartitionNumber == partition)?.Let(Clone));
        }
    }

    public Task<IReadOnlyList<ActiveMount>> GetMountsForDiskAsync(int diskIndex, CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            var mounts = _mounts.Where(m => m.DiskIndex == diskIndex).Select(Clone).ToList().AsReadOnly();
            return Task.FromResult<IReadOnlyList<ActiveMount>>(mounts);
        }
    }

    public Task<ActiveMount?> GetMountForDriveLetterAsync(char driveLetter, CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            return Task.FromResult(_mounts.FirstOrDefault(m => char.ToUpperInvariant(m.DriveLetter) == char.ToUpperInvariant(driveLetter))?.Let(Clone));
        }
    }

    public Task<bool> IsDiskMountedAsync(int diskIndex, CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            return Task.FromResult(_mounts.Any(m => m.DiskIndex == diskIndex));
        }
    }

    public Task<bool> IsDriveLetterInUseAsync(char driveLetter, CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            return Task.FromResult(_mounts.Any(m => char.ToUpperInvariant(m.DriveLetter) == char.ToUpperInvariant(driveLetter)));
        }
    }

    public Task<IReadOnlyList<ActiveMount>> ReconcileMountStateAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<ActiveMount>>(Array.Empty<ActiveMount>());
    }

    public Task ClearAllMountsAsync(CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            _mounts.Clear();
        }
        return Task.CompletedTask;
    }

    private static ActiveMount Clone(ActiveMount mount)
    {
        return new ActiveMount
        {
            Id = mount.Id,
            DiskIndex = mount.DiskIndex,
            PartitionNumber = mount.PartitionNumber,
            DriveLetter = mount.DriveLetter,
            DistroName = mount.DistroName,
            MountPathLinux = mount.MountPathLinux,
            MountPathUNC = mount.MountPathUNC,
            MountedAt = mount.MountedAt,
            IsVerified = mount.IsVerified,
            LastVerified = mount.LastVerified
        };
    }
}

internal sealed class TestMountHistoryService : IMountHistoryService
{
    private readonly List<MountHistoryEntry> _entries = [];
    private readonly object _sync = new();

    public Task<IReadOnlyList<MountHistoryEntry>> GetHistoryAsync(CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            return Task.FromResult<IReadOnlyList<MountHistoryEntry>>(_entries.ToList().AsReadOnly());
        }
    }

    public Task AddEntryAsync(MountHistoryEntry entry, CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            _entries.Insert(0, entry);
        }
        return Task.CompletedTask;
    }

    public Task ClearHistoryAsync(CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            _entries.Clear();
        }
        return Task.CompletedTask;
    }

    public Task<MountHistoryEntry?> GetLastMountForDiskAsync(int diskIndex, CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            var entry = _entries.FirstOrDefault(e =>
                e.OperationType == MountHistoryOperationType.Mount &&
                e.DiskIndex == diskIndex);
            return Task.FromResult(entry);
        }
    }
}

internal static class TestModeExtensions
{
    internal static TOut? Let<TIn, TOut>(this TIn? value, Func<TIn, TOut> map)
        where TIn : class
        where TOut : class
    {
        return value == null ? null : map(value);
    }
}
