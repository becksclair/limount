using LiMount.Core.Models;

namespace LiMount.Core.Interfaces;

/// <summary>
/// Detects host capabilities required by virtualization setup.
/// </summary>
public interface IPlatformCapabilityService
{
    /// <summary>
    /// Detects host virtualization and WSL capabilities.
    /// </summary>
    Task<PlatformCapabilities> DetectAsync(CancellationToken cancellationToken = default);
}
