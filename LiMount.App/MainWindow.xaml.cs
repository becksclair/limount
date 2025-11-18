using System.Windows;
using LiMount.App.ViewModels;

namespace LiMount.App;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();

        // Set the DataContext to injected ViewModel
        DataContext = viewModel;
    }
}
