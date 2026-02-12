using LiMount.Core.Models;

namespace LiMount.WinUI.Services;

/// <summary>
/// Executes first-run setup wizard flow and persists resulting settings.
/// </summary>
public interface ISetupWizardService
{
    /// <summary>
    /// Ensures setup has been completed, displaying wizard when needed.
    /// </summary>
    Task<SetupWizardResult> EnsureSetupAsync(bool forceWizard = false, CancellationToken cancellationToken = default);
}

/// <summary>
/// Result for setup wizard orchestration.
/// </summary>
public sealed class SetupWizardResult
{
    public bool IsCompleted { get; init; }

    public bool WasCanceled { get; init; }

    public UserSettings Settings { get; init; } = new();

    public PlatformCapabilities Capabilities { get; init; } = new();
}
