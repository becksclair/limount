using System;
using System.Linq;
using LiMount.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace LiMount.WinUI.Views;

/// <summary>
/// Minimal shell page for the WinUI NativeAOT spike.
/// </summary>
public sealed partial class MainPage : Page
{
    private readonly IEnvironmentValidationService _environmentValidation;

    public MainPage()
    {
        _environmentValidation = App.Services.GetRequiredService<IEnvironmentValidationService>();
        InitializeComponent();
    }

    private async void OnValidateClicked(object sender, RoutedEventArgs e)
    {
        StatusText.Text = "Validating environment...";
        DetailsText.Text = string.Empty;

        try
        {
            var result = await _environmentValidation.ValidateEnvironmentAsync();

            if (result.IsValid)
            {
                StatusText.Text = "Environment looks good for LiMount (WSL + Windows build).";
                DetailsText.Text = $"Build: {result.WindowsVersion} ({result.WindowsBuildNumber}), Distros: {string.Join(", ", result.InstalledDistros)}";
            }
            else
            {
                StatusText.Text = "Environment validation failed.";
                var errors = string.Join(Environment.NewLine, result.Errors ?? Enumerable.Empty<string>());
                var suggestions = string.Join(Environment.NewLine, result.Suggestions ?? Enumerable.Empty<string>());
                DetailsText.Text = $"{errors}{Environment.NewLine}{suggestions}";
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = "Validation threw an exception.";
            DetailsText.Text = ex.Message;
        }
    }
}
