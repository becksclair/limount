using Microsoft.UI.Xaml;

namespace LiMount.WinUI.Services;

public interface IXamlRootProvider
{
    XamlRoot? GetXamlRoot();
    void SetXamlRoot(XamlRoot root);
    Task<XamlRoot> WaitForXamlRootAsync(int timeoutMs = 5000);
}

public sealed class XamlRootProvider : IXamlRootProvider
{
    private XamlRoot? _xamlRoot;
    private readonly TaskCompletionSource<XamlRoot> _xamlRootReady = new();

    public XamlRoot? GetXamlRoot() => _xamlRoot;

    public void SetXamlRoot(XamlRoot root)
    {
        _xamlRoot = root;
        _xamlRootReady.TrySetResult(root);
    }

    public async Task<XamlRoot> WaitForXamlRootAsync(int timeoutMs = 5000)
    {
        if (_xamlRoot != null)
            return _xamlRoot;

        using var cts = new CancellationTokenSource(timeoutMs);
        try
        {
            return await _xamlRootReady.Task.WaitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            throw new InvalidOperationException(
                $"XamlRoot not available after {timeoutMs}ms. Ensure the main window is loaded before showing dialogs.");
        }
    }
}
