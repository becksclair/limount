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

    /// <summary>
    /// Initializes the application and configures dependency injection services.
    /// </summary>
    /// <remarks>
    /// Creates a ServiceCollection, registers services via ConfigureServices, builds the ServiceProvider, and stores it for later use.
    /// </remarks>
    public App()
    {
        // Set up dependency injection
        var services = new ServiceCollection();
        ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider();
    }

    /// <summary>
    /// Configures the application's dependency injection container by registering logging, core services, orchestrators, view models, and the main window.
    /// </summary>
    /// <param name="services">The service collection to which application services and components will be added.</param>
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

    /// <summary>
    /// Initializes application startup and shows the main window resolved from the dependency injection container.
    /// </summary>
    /// <param name="e">Startup event data.</param>
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Create and show the main window using DI
        var mainWindow = _serviceProvider?.GetRequiredService<MainWindow>();
        mainWindow?.Show();
    }

    /// <summary>
    /// Performs application shutdown tasks, disposing the dependency injection container before completing exit processing.
    /// </summary>
    /// <param name="e">Event data for the application exit.</param>
    protected override void OnExit(ExitEventArgs e)
    {
        _serviceProvider?.Dispose();
        base.OnExit(e);
    }
}