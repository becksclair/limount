namespace LiMount.WinUI.Services;

/// <summary>
/// Runs Hyper-V enablement command with elevation.
/// </summary>
public interface IHyperVEnableService
{
    /// <summary>
    /// Attempts to enable Hyper-V on the local machine.
    /// </summary>
    Task<HyperVEnableResult> EnableAsync(CancellationToken cancellationToken = default);
}
