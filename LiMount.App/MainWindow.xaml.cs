using System.Windows;
using LiMount.App.ViewModels;

namespace LiMount.App;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>
    /// Initializes the window's UI components and assigns the provided view model as the window's DataContext.
    /// </summary>
    /// <param name="viewModel">The view model to use as the window's DataContext.</param>
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();

        // Set the DataContext to injected ViewModel
        DataContext = viewModel;
    }
}