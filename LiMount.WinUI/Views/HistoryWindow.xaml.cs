using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using WinRT.Interop;

namespace LiMount.WinUI.Views;

/// <summary>
/// Window that hosts the HistoryPage for displaying mount history.
/// </summary>
public sealed class HistoryWindow : Window
{
    public HistoryWindow(HistoryPage historyPage)
    {
        Title = "Mount History - LiMount";

        // Set window size via AppWindow (WinUI 3 pattern)
        var hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);
        appWindow.Resize(new Windows.Graphics.SizeInt32(900, 600));

        // Wire up close request from page
        historyPage.CloseRequested += (_, _) => Close();

        Content = historyPage;
    }
}
