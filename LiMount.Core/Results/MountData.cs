namespace LiMount.Core.Results;

/// <summary>
/// Data payload for successful mount operations.
/// Contains all information about a mounted partition.
/// </summary>
/// <param name="DiskIndex">The physical disk index.</param>
/// <param name="Partition">The partition number (1-based).</param>
/// <param name="DriveLetter">The Windows drive letter assigned, or null if not mapped.</param>
/// <param name="DistroName">The WSL distribution used for mounting.</param>
/// <param name="MountPathLinux">The Linux mount path (e.g., /mnt/wsl/PHYSICALDRIVE0p1).</param>
/// <param name="MountPathUNC">The Windows UNC path (e.g., \\wsl$\Ubuntu\mnt\wsl\PHYSICALDRIVE0p1).</param>
public sealed record MountData(
    int DiskIndex,
    int Partition,
    char? DriveLetter,
    string DistroName,
    string MountPathLinux,
    string MountPathUNC);

/// <summary>
/// Data payload for successful unmount operations.
/// </summary>
/// <param name="DiskIndex">The physical disk index that was unmounted.</param>
/// <param name="DriveLetter">The drive letter that was unmapped, or null if no mapping existed.</param>
public sealed record UnmountData(
    int DiskIndex,
    char? DriveLetter);

/// <summary>
/// Data payload for successful drive mapping operations.
/// </summary>
/// <param name="DriveLetter">The drive letter that was assigned.</param>
/// <param name="TargetUNC">The UNC path that was mapped.</param>
public sealed record MappingData(
    char DriveLetter,
    string TargetUNC);

/// <summary>
/// Data payload for environment validation.
/// </summary>
/// <param name="InstalledDistros">List of installed WSL distributions.</param>
/// <param name="WindowsVersion">The Windows version.</param>
/// <param name="WindowsBuildNumber">The Windows build number.</param>
public sealed record EnvironmentData(
    IReadOnlyList<string> InstalledDistros,
    Version? WindowsVersion,
    int WindowsBuildNumber);
