using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using LiMount.App.ViewModels;
using LiMount.App.Services;
using LiMount.Core.Interfaces;
using LiMount.Core.Services;
using LiMount.Core.Configuration;
using Serilog;
using System.IO;

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
    /// Logging is configured with the Debug provider for development and conditional file logging for production.
    /// </remarks>
    private void ConfigureServices(IServiceCollection services)
    {
        // Build configuration from appsettings.json
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .Build();

        // Register configuration
        services.AddSingleton<IConfiguration>(configuration);
        services.Configure<LiMountConfiguration>(configuration.GetSection(LiMountConfiguration.SectionName));

        // Configure Serilog for file logging
        var logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                   "LiMount", "logs", "limount-.log");

        // Check if we're in production (not debugging) or explicitly enabled via environment variable
        var isProduction = !System.Diagnostics.Debugger.IsAttached ||
                           Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")?.Equals("Production", StringComparison.OrdinalIgnoreCase) == true;

        // Configure Serilog with error handling
        var serilogConfig = new LoggerConfiguration()
            .MinimumLevel.Information()
            .Enrich.FromLogContext();

        // Add file logging for production
        if (isProduction)
        {
            try
            {
                // Ensure log directory exists
                var logDirectory = Path.GetDirectoryName(logPath);
                if (!string.IsNullOrEmpty(logDirectory))
                {
                    Directory.CreateDirectory(logDirectory);
                }

                serilogConfig.WriteTo.File(
                    path: logPath,
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 7, // Keep last 7 days
                    fileSizeLimitBytes: 10 * 1024 * 1024, // 10MB per file
                    rollOnFileSizeLimit: true,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}"
                );
            }
            catch (Exception ex)
            {
                // Fall back to debug-only logging if file logging setup fails
                System.Diagnostics.Debug.WriteLine($"Failed to configure file logging: {ex.Message}");
            }
        }

        var logger = serilogConfig.CreateLogger();

        // Register logging
        services.AddLogging(builder =>
        {
            builder.AddDebug();
            builder.AddSerilog(logger, dispose: true);
            builder.SetMinimumLevel(LogLevel.Information);
        });

        // Register Core services
        services.AddSingleton<IDiskEnumerationService, DiskEnumerationService>();
        services.AddSingleton<IDriveLetterService, DriveLetterService>();
        services.AddSingleton<IScriptExecutor, ScriptExecutor>();
        services.AddSingleton<IMountHistoryService, MountHistoryService>();
        services.AddSingleton<IMountStateService, MountStateService>();

        // Register App services
        services.AddSingleton<IDialogService, DialogService>();

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

        // Log application startup and version for diagnostics
        var logger = _serviceProvider?.GetService<ILogger<App>>();
        logger?.LogInformation("LiMount application started successfully. Version: {Version}",
            System.Reflection.Assembly.GetExecutingAssembly().GetName().Version);

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