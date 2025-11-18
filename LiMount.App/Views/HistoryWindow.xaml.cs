using System.Windows;
using LiMount.App.ViewModels;

namespace LiMount.App.Views;

/// <summary>
/// Interaction logic for HistoryWindow.xaml
/// </summary>
public partial class HistoryWindow : Window
{
    /// <summary>
    /// Initializes a new instance of HistoryWindow with the provided ViewModel.
    /// </summary>
    /// <param name="viewModel">The HistoryViewModel to use as DataContext.</param>
    public HistoryWindow(HistoryViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        // Initialize data when window loads
        Loaded += async (sender, e) =>
        {
            await viewModel.LoadHistoryAsync();
        };
    }

    /// <summary>
    /// Closes the window when the Close button is clicked.
    /// </summary>
    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
