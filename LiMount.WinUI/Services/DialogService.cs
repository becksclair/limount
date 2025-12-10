using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using LiMount.Core.Abstractions;

namespace LiMount.WinUI.Services;

/// <summary>
/// WinUI implementation of IDialogService using ContentDialog.
/// </summary>
public sealed class DialogService : IDialogService
{
    private readonly IXamlRootProvider _xamlRootProvider;

    public DialogService(IXamlRootProvider xamlRootProvider)
    {
        _xamlRootProvider = xamlRootProvider;
    }

    public async Task<bool> ConfirmAsync(string message, string title, DialogType dialogType = DialogType.Warning)
    {
        var dialog = await CreateDialogAsync(message, title, dialogType);
        dialog.PrimaryButtonText = "Yes";
        dialog.CloseButtonText = "No";
        dialog.DefaultButton = ContentDialogButton.Primary;

        var result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary;
    }

    public async Task ShowErrorAsync(string message, string title = "Error")
    {
        var dialog = await CreateDialogAsync(message, title, DialogType.Error);
        dialog.CloseButtonText = "OK";
        await dialog.ShowAsync();
    }

    public async Task ShowInfoAsync(string message, string title = "Information")
    {
        var dialog = await CreateDialogAsync(message, title, DialogType.Information);
        dialog.CloseButtonText = "OK";
        await dialog.ShowAsync();
    }

    public async Task ShowWarningAsync(string message, string title = "Warning")
    {
        var dialog = await CreateDialogAsync(message, title, DialogType.Warning);
        dialog.CloseButtonText = "OK";
        await dialog.ShowAsync();
    }

    private async Task<ContentDialog> CreateDialogAsync(string message, string title, DialogType dialogType)
    {
        var xamlRoot = await _xamlRootProvider.WaitForXamlRootAsync();

        var dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            XamlRoot = xamlRoot
        };

        dialog.Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style;

        dialog.DefaultButton = ContentDialogButton.Close;

        dialog.CloseButtonText = "Close";

        dialog.PrimaryButtonText = string.Empty;

        return dialog;
    }
}
