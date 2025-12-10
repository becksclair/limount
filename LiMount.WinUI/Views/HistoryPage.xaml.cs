using LiMount.WinUI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace LiMount.WinUI.Views;

/// <summary>
/// Page displaying mount history in a DataGrid.
/// </summary>
public sealed partial class HistoryPage : Page
{
    public HistoryViewModel ViewModel { get; }

    /// <summary>
    /// Event raised when the Close button is clicked.
    /// </summary>
    public event EventHandler? CloseRequested;

    public HistoryPage(HistoryViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Note: LoadHistoryAsync already has its own try-catch with error dialog.
        // This outer catch is defense-in-depth for truly unexpected failures.
        try
        {
            await ViewModel.LoadHistoryAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Critical error loading history: {ex}");
            // Surface the error to the user since ViewModel's handler must have failed
            ViewModel.StatusMessage = $"Critical error: {ex.Message}";
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }
}
