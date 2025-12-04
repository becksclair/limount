using LiMount.Core.Configuration;
using LiMount.Core.Interfaces;
using LiMount.Core.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Serilog;
using System.Diagnostics.CodeAnalysis;

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

                // Core services
                services.AddSingleton<IDiskEnumerationService, DiskEnumerationService>();
                services.AddSingleton<IDriveLetterService, DriveLetterService>();
                services.AddSingleton<IScriptExecutor, ScriptExecutor>();
                services.AddSingleton<IMountHistoryService, MountHistoryService>();
                services.AddSingleton<IMountStateService, MountStateService>();
                services.AddSingleton<IEnvironmentValidationService, EnvironmentValidationService>();

                // Views
                services.AddTransient<Views.MainPage>();
            })
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddDebug();
                logging.SetMinimumLevel(LogLevel.Information);
            })
            .UseSerilog((context, services, loggerConfiguration) =>
            {
                loggerConfiguration
                    .MinimumLevel.Information()
                    .WriteTo.Console();
            });

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        HostInstance?.Start();

        _window ??= new Window
        {
            Content = Services.GetRequiredService<Views.MainPage>()
        };

        _window.Activate();
    }

}
