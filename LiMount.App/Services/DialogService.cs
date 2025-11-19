using System.Windows;

namespace LiMount.App.Services;

/// <summary>
/// WPF implementation of IDialogService using MessageBox.
/// </summary>
public class DialogService : IDialogService
{
    public Task<bool> ConfirmAsync(string message, string title, DialogType dialogType = DialogType.Warning)
    {
        var icon = dialogType switch
        {
            DialogType.Information => MessageBoxImage.Information,
            DialogType.Warning => MessageBoxImage.Warning,
            DialogType.Error => MessageBoxImage.Error,
            _ => MessageBoxImage.Question
        };

        var result = MessageBox.Show(message, title, MessageBoxButton.YesNo, icon);
        return Task.FromResult(result == MessageBoxResult.Yes);
    }

    public Task ShowErrorAsync(string message, string title = "Error")
    {
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
        return Task.CompletedTask;
    }

    public Task ShowInfoAsync(string message, string title = "Information")
    {
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
        return Task.CompletedTask;
    }

    public Task ShowWarningAsync(string message, string title = "Warning")
    {
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
        return Task.CompletedTask;
    }
}
