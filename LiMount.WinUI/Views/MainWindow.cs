using LiMount.WinUI.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Windowing;
using WinRT.Interop;

namespace LiMount.WinUI.Views;

public sealed class MainWindow : Window
{
    private const int DefaultWindowWidth = 900;
    private const int DefaultWindowHeight = 750;

    public MainWindow(
        MainPage mainPage,
        IXamlRootProvider xamlRootProvider,
        UiDispatcher uiDispatcher)
    {
        Title = "LiMount - WSL Disk Mounter";
        Content = mainPage;

        uiDispatcher.Initialize(DispatcherQueue);
        mainPage.Loaded += (_, _) =>
        {
            if (mainPage.Content is FrameworkElement element && element.XamlRoot != null)
            {
                xamlRootProvider.SetXamlRoot(element.XamlRoot);
            }
            else if (mainPage.XamlRoot != null)
            {
                xamlRootProvider.SetXamlRoot(mainPage.XamlRoot);
            }
        };

        // Set window size via AppWindow (WinUI 3 pattern)
        var hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);
        appWindow.Resize(new Windows.Graphics.SizeInt32(DefaultWindowWidth, DefaultWindowHeight));
    }
}
