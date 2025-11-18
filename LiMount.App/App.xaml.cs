using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using LiMount.App.ViewModels;
using LiMount.Core.Interfaces;
using LiMount.Core.Services;

namespace LiMount.App;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private ServiceProvider? _serviceProvider;

    public App()
    {
        // Set up dependency injection
        var services = new ServiceCollection();
        ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider();
    }

    private void ConfigureServices(IServiceCollection services)
    {
        // Register logging
        services.AddLogging(builder =>
        {
            builder.AddDebug();
            builder.SetMinimumLevel(LogLevel.Information);
        });

        // Register Core services
        services.AddSingleton<IDiskEnumerationService, DiskEnumerationService>();
        services.AddSingleton<IDriveLetterService, DriveLetterService>();
        services.AddSingleton<IScriptExecutor, ScriptExecutor>();

        // Register orchestrators
        services.AddTransient<IMountOrchestrator, MountOrchestrator>();
        services.AddTransient<IUnmountOrchestrator, UnmountOrchestrator>();

        // Register ViewModels
        services.AddTransient<MainViewModel>();

        // Register MainWindow
        services.AddTransient<MainWindow>();
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Create and show the main window using DI
        var mainWindow = _serviceProvider?.GetRequiredService<MainWindow>();
        mainWindow?.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _serviceProvider?.Dispose();
        base.OnExit(e);
    }
}
