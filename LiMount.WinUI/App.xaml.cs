using LiMount.Core.Abstractions;
using LiMount.Core.Configuration;
using LiMount.Core.Interfaces;
using LiMount.Core.Services;
using LiMount.WinUI.Services;
using LiMount.WinUI.TestMode;
using LiMount.WinUI.ViewModels;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.UI.Xaml;
using Serilog;
using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;

namespace LiMount.WinUI;

/// <summary>
/// WinUI 3 bootstrapper that wires the LiMount.Core services via the generic host.
/// </summary>
public partial class App : Application
{
    public static IHost? HostInstance { get; private set; }
    public static IServiceProvider Services =>
        HostInstance?.Services ?? throw new InvalidOperationException("Host not initialized");

    private Window? _window;

    public App()
    {
        InitializeComponent();
        HostInstance = CreateHostBuilder().Build();
    }

    private static IHostBuilder CreateHostBuilder() =>
        Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder()
            .UseContentRoot(AppContext.BaseDirectory)
            .ConfigureAppConfiguration((context, builder) =>
            {
                builder.SetBasePath(AppContext.BaseDirectory);
                builder.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
            })
            .ConfigureServices((context, services) =>
            {
#pragma warning disable IL2026 // Trimming
#pragma warning disable IL3050 // AOT
                services.Configure<LiMountConfiguration>(context.Configuration.GetSection(LiMountConfiguration.SectionName));
#pragma warning restore IL3050
#pragma warning restore IL2026

                var isTestMode = string.Equals(
                    Environment.GetEnvironmentVariable("LIMOUNT_TEST_MODE"),
                    "1",
                    StringComparison.OrdinalIgnoreCase);

                if (isTestMode)
                {
                    services.PostConfigure<LiMountConfiguration>(config =>
                    {
                        config.Initialization.AutoReconcileMounts = false;
                        config.Initialization.AutoDetectSystemMounts = false;
                    });
                    services.AddLiMountTestModeServices();
                }
                else
                {
                    // Core services
                    services.AddSingleton<IDiskEnumerationService, DiskEnumerationService>();
                    services.AddSingleton<IDriveLetterService, DriveLetterService>();
                    services.AddSingleton<IMountHistoryService, MountHistoryService>();
                    services.AddSingleton<IMountStateService, MountStateService>();
                    services.AddSingleton<IEnvironmentValidationService, EnvironmentValidationService>();
                    services.AddTransient<IMountOrchestrator, MountOrchestrator>();
                    services.AddTransient<IUnmountOrchestrator, UnmountOrchestrator>();

                    // Register ScriptExecutor with all focused interfaces
                    services.AddSingleton<ScriptExecutor>();
                    services.AddSingleton<IMountScriptService>(sp => sp.GetRequiredService<ScriptExecutor>());
                    services.AddSingleton<IDriveMappingService>(sp => sp.GetRequiredService<ScriptExecutor>());
                    services.AddSingleton<IFilesystemDetectionService>(sp => sp.GetRequiredService<ScriptExecutor>());
                }

#pragma warning disable CS0618 // IScriptExecutor is obsolete - kept for backward compatibility
                if (!isTestMode)
                {
                    services.AddSingleton<IScriptExecutor>(sp => sp.GetRequiredService<ScriptExecutor>());
                }
#pragma warning restore CS0618

                // UI infrastructure
                services.AddSingleton<IXamlRootProvider, XamlRootProvider>();
                services.AddSingleton<UiDispatcher>();
                services.AddSingleton<IUiDispatcher>(sp => sp.GetRequiredService<UiDispatcher>());
                services.AddSingleton<IDialogService, DialogService>();

                // ViewModels
                services.AddTransient<MainViewModel.MountingServices>(sp => new MainViewModel.MountingServices(
                    sp.GetRequiredService<IDiskEnumerationService>(),
                    sp.GetRequiredService<IDriveLetterService>(),
                    sp.GetRequiredService<IMountOrchestrator>(),
                    sp.GetRequiredService<IUnmountOrchestrator>(),
                    sp.GetRequiredService<IMountStateService>(),
                    sp.GetRequiredService<IFilesystemDetectionService>()));

                services.AddTransient<MainViewModel.AppServices>(sp => new MainViewModel.AppServices(
                    sp.GetRequiredService<IEnvironmentValidationService>(),
                    sp.GetRequiredService<IDialogService>(),
                    sp.GetRequiredService<ILogger<MainViewModel>>(),
                    sp.GetRequiredService<IOptions<LiMountConfiguration>>()));

                services.AddTransient<MainViewModel>();
                services.AddTransient<HistoryViewModel>();

                // Views / Windows
                services.AddTransient<Views.MainPage>();
                services.AddTransient<Views.MainWindow>();
                services.AddTransient<Views.HistoryPage>();
                services.AddTransient<Views.HistoryWindow>();
                services.AddTransient<Func<Views.HistoryWindow>>(sp => () => sp.GetRequiredService<Views.HistoryWindow>());
            })
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddDebug();
                logging.SetMinimumLevel(LogLevel.Information);
            })
            .UseSerilog((context, services, loggerConfiguration) =>
            {
                // Configure log path in LocalApplicationData
                var logPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "LiMount", "logs", "limount-.log");

                // Check if we're in production (not debugging) or explicitly enabled
                var isProduction = !System.Diagnostics.Debugger.IsAttached ||
                    Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")?.Equals("Production", StringComparison.OrdinalIgnoreCase) == true;

                loggerConfiguration
                    .MinimumLevel.Information()
                    .Enrich.FromLogContext()
                    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}");

                // Add file logging for production
                if (isProduction)
                {
                    try
                    {
                        var logDirectory = Path.GetDirectoryName(logPath);
                        if (!string.IsNullOrEmpty(logDirectory))
                        {
                            Directory.CreateDirectory(logDirectory);
                        }

                        loggerConfiguration.WriteTo.File(
                            path: logPath,
                            rollingInterval: Serilog.RollingInterval.Day,
                            retainedFileCountLimit: 7,
                            fileSizeLimitBytes: 10 * 1024 * 1024,
                            rollOnFileSizeLimit: true,
                            outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}");
                    }
                    catch (Exception ex)
                    {
                        // Log to stderr so it's visible even without file logging
                        Console.Error.WriteLine($"WARNING: Failed to configure file logging: {ex.Message}");
                        Console.Error.WriteLine($"Log path attempted: {logPath}");
                        System.Diagnostics.Debug.WriteLine($"Failed to configure file logging: {ex.Message}");
                    }
                }
            });

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        HostInstance?.Start();

        _window ??= Services.GetRequiredService<Views.MainWindow>();

        _window.Activate();
    }

}
