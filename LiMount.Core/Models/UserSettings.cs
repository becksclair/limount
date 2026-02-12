namespace LiMount.Core.Models;

/// <summary>
/// User-level settings for backend preference, fallback policy, and setup completion.
/// </summary>
public sealed class UserSettings
{
    /// <summary>
    /// Schema version for settings migrations.
    /// </summary>
    public int Version { get; set; } = 1;

    /// <summary>
    /// True when the setup wizard has been completed at least once.
    /// </summary>
    public bool HasCompletedSetup { get; set; }

    /// <summary>
    /// Preferred mount backend strategy.
    /// </summary>
    public MountBackendPreference BackendPreference { get; set; } = MountBackendPreference.WslPreferred;

    /// <summary>
    /// VM fallback policy for WSL mount failures.
    /// </summary>
    public VmFallbackPolicy VmFallbackPolicy { get; set; } = VmFallbackPolicy.Disabled;

    /// <summary>
    /// Preferred hypervisor provider.
    /// </summary>
    public HypervisorSelection Hypervisor { get; set; } = HypervisorSelection.Auto;

    /// <summary>
    /// Preferred Windows access mode.
    /// </summary>
    public WindowsAccessMode AccessMode { get; set; } = WindowsAccessMode.NetworkLocation;

    /// <summary>
    /// VM appliance configuration.
    /// </summary>
    public VmApplianceSettings VmAppliance { get; set; } = new();

    /// <summary>
    /// Guest authentication configuration.
    /// </summary>
    public GuestAuthSettings GuestAuth { get; set; } = new();
}

/// <summary>
/// Backend preference for mount orchestration.
/// </summary>
public enum MountBackendPreference
{
    WslPreferred = 0,
    VmPreferred = 1,
    VmOnly = 2
}

/// <summary>
/// VM fallback policy options.
/// </summary>
public enum VmFallbackPolicy
{
    Disabled = 0,
    OnFsIncompatibility = 1,
    OnSpecificErrors = 2
}

/// <summary>
/// Hypervisor provider selection.
/// </summary>
public enum HypervisorSelection
{
    HyperV = 0,
    VMware = 1,
    VirtualBox = 2,
    Auto = 3
}

/// <summary>
/// How mounted data should be surfaced in Windows.
/// </summary>
public enum WindowsAccessMode
{
    NetworkLocation = 0,
    DriveLetterLegacy = 1,
    None = 2
}

/// <summary>
/// Basic VM appliance settings captured by the wizard.
/// </summary>
public sealed class VmApplianceSettings
{
    /// <summary>
    /// Appliance VM name.
    /// </summary>
    public string VmName { get; set; } = "LiMount-Appliance";

    /// <summary>
    /// Optional VM disk storage path.
    /// </summary>
    public string? StoragePath { get; set; }

    /// <summary>
    /// Whether to use an existing VM instead of an appliance VM.
    /// </summary>
    public bool UseExistingVm { get; set; }
}

/// <summary>
/// Guest authentication settings for VM workflows.
/// </summary>
public sealed class GuestAuthSettings
{
    /// <summary>
    /// Guest hostname or IP.
    /// </summary>
    public string? Host { get; set; }

    /// <summary>
    /// Guest username.
    /// </summary>
    public string? Username { get; set; }

    /// <summary>
    /// Indicates SSH key based auth should be preferred.
    /// </summary>
    public bool UseSshKey { get; set; } = true;
}
