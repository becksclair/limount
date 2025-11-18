using System.Windows;
using LiMount.App.ViewModels;

namespace LiMount.App;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // Set the DataContext to MainViewModel
        DataContext = new MainViewModel();
    }
}
