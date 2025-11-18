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
    /// Initializes the application and configures dependency injection.
    /// </summary>
    /// <remarks>
    /// Registers services, builds the service provider, and stores it for resolving application dependencies.
    /// </remarks>
    public App()
    {
        // Set up dependency injection
        var services = new ServiceCollection();
        ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider();
    }

    /// <summary>
    /// Registers application services, view models, windows, and logging into the provided dependency injection service collection.
    /// </summary>
    /// <remarks>
    /// Core services are registered as singletons; orchestrators, view models, and the main window are registered as transient.
    /// Logging is configured with the Debug provider and a minimum level of Information.
    /// </remarks>
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
    /// Initializes application startup by resolving the main window from the dependency-injection container and displaying it.
    /// </summary>
    /// <param name="e">Event data for the startup event.</param>
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Create and show the main window using DI
        var mainWindow = _serviceProvider?.GetRequiredService<MainWindow>();
        mainWindow?.Show();
    }

    /// <summary>
    /// Performs application shutdown by disposing the dependency injection service provider and then executing base shutdown processing.
    /// </summary>
    /// <param name="e">Event data for the application exit.</param>
    protected override void OnExit(ExitEventArgs e)
    {
        _serviceProvider?.Dispose();
        base.OnExit(e);
    }
}