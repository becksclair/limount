namespace LiMount.WinUI.Services;

/// <summary>
/// Opens external links in the user's default browser.
/// </summary>
public interface ILinkOpenerService
{
    /// <summary>
    /// Attempts to open the provided URL.
    /// </summary>
    bool TryOpen(string url, out string? errorMessage);
}
