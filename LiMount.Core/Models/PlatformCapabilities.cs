namespace LiMount.Core.Models;

/// <summary>
/// Platform capability snapshot used by setup wizard decisions.
/// </summary>
public sealed class PlatformCapabilities
{
    /// <summary>
    /// Detected Windows edition (for example Home/Pro/Enterprise).
    /// </summary>
    public string WindowsEdition { get; set; } = string.Empty;

    /// <summary>
    /// Whether Hyper-V is supported on this edition.
    /// </summary>
    public bool HyperVSupported { get; set; }

    /// <summary>
    /// Whether Hyper-V feature appears enabled.
    /// </summary>
    public bool HyperVEnabled { get; set; }

    /// <summary>
    /// Whether Hyper-V management tooling is available.
    /// </summary>
    public bool HyperVCmdletsAvailable { get; set; }

    /// <summary>
    /// Whether WSL appears installed.
    /// </summary>
    public bool WslInstalled { get; set; }

    /// <summary>
    /// Whether `wsl --mount` support is detected.
    /// </summary>
    public bool WslMountSupported { get; set; }

    /// <summary>
    /// Whether at least one default/available distro is detected.
    /// </summary>
    public bool DefaultDistroPresent { get; set; }

    /// <summary>
    /// Whether VMware tooling is detected.
    /// </summary>
    public bool VmwareInstalled { get; set; }

    /// <summary>
    /// Whether VirtualBox tooling is detected.
    /// </summary>
    public bool VirtualBoxInstalled { get; set; }

    /// <summary>
    /// Number of logical CPU cores on host.
    /// </summary>
    public int HostCpuCores { get; set; }

    /// <summary>
    /// Approximate total physical RAM in bytes.
    /// </summary>
    public long HostMemoryBytes { get; set; }

    /// <summary>
    /// Optional reason shown when Hyper-V is unavailable.
    /// </summary>
    public string? HyperVUnavailableReason { get; set; }

    /// <summary>
    /// Optional reason shown when VMware is unavailable.
    /// </summary>
    public string? VmwareUnavailableReason { get; set; }

    /// <summary>
    /// Optional reason shown when VirtualBox is unavailable.
    /// </summary>
    public string? VirtualBoxUnavailableReason { get; set; }

    /// <summary>
    /// Optional reason shown when WSL mount path is unavailable.
    /// </summary>
    public string? WslUnavailableReason { get; set; }
}
