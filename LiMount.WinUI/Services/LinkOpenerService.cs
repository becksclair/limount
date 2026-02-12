using System.Diagnostics;

namespace LiMount.WinUI.Services;

/// <summary>
/// Default URL opener service based on shell execution.
/// </summary>
public sealed class LinkOpenerService : ILinkOpenerService
{
    public bool TryOpen(string url, out string? errorMessage)
    {
        errorMessage = null;

        if (string.IsNullOrWhiteSpace(url))
        {
            errorMessage = "URL is required.";
            return false;
        }

        try
        {
            Process.Start(new ProcessStartInfo(url)
            {
                UseShellExecute = true
            });
            return true;
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            return false;
        }
    }
}
