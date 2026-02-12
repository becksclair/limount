using System;
using LiMount.WinUI.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Windowing;
using WinRT.Interop;

namespace LiMount.WinUI.Views;

public sealed class MainWindow : Window
{
    private const int DefaultWindowWidth = 900;
    private const int DefaultWindowHeight = 750;
    private const int MinimumWindowWidth = 900;
    private const int MinimumWindowHeight = 750;

    private readonly AppWindow _appWindow;
    private bool _isEnforcingMinimumSize;

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
        _appWindow = AppWindow.GetFromWindowId(windowId);
        _appWindow.Changed += AppWindow_Changed;
        Closed += (_, _) => _appWindow.Changed -= AppWindow_Changed;
        _appWindow.Resize(new Windows.Graphics.SizeInt32(DefaultWindowWidth, DefaultWindowHeight));
    }

    private void AppWindow_Changed(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (!args.DidSizeChange || _isEnforcingMinimumSize)
        {
            return;
        }

        var currentSize = sender.Size;
        var clampedWidth = Math.Max(currentSize.Width, MinimumWindowWidth);
        var clampedHeight = Math.Max(currentSize.Height, MinimumWindowHeight);

        if (clampedWidth == currentSize.Width && clampedHeight == currentSize.Height)
        {
            return;
        }

        try
        {
            _isEnforcingMinimumSize = true;
            sender.Resize(new Windows.Graphics.SizeInt32(clampedWidth, clampedHeight));
        }
        finally
        {
            _isEnforcingMinimumSize = false;
        }
    }
}
