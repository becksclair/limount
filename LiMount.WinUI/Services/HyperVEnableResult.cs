namespace LiMount.WinUI.Services;

/// <summary>
/// Outcome for Hyper-V enablement action.
/// </summary>
public sealed class HyperVEnableResult
{
    public bool Success { get; init; }

    public bool RequiresRestart { get; init; }

    public bool WasCanceledByUser { get; init; }

    public string? ErrorMessage { get; init; }

    public static HyperVEnableResult Completed(bool requiresRestart) => new()
    {
        Success = true,
        RequiresRestart = requiresRestart
    };

    public static HyperVEnableResult Canceled() => new()
    {
        Success = false,
        WasCanceledByUser = true
    };

    public static HyperVEnableResult Failed(string? error) => new()
    {
        Success = false,
        ErrorMessage = string.IsNullOrWhiteSpace(error) ? "Unknown failure enabling Hyper-V." : error
    };
}
