using LiMount.Core.Abstractions;
using LiMount.Core.Configuration;
using LiMount.WinUI.Services;
using LiMount.WinUI.ViewModels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace LiMount.WinUI.Views;

public sealed partial class MainPage : Page
{
    private readonly MainViewModel _viewModel;
    private readonly IDialogService _dialogService;
    private readonly ISetupWizardService _setupWizardService;
    private readonly InitializationConfig _config;
    private readonly ILogger<MainPage> _logger;
    private CancellationTokenSource? _cancellationTokenSource;

    public MainPage(
        MainViewModel viewModel,
        IDialogService dialogService,
        ISetupWizardService setupWizardService,
        IOptions<LiMountConfiguration> config,
        ILogger<MainPage> logger)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _dialogService = dialogService;
        _setupWizardService = setupWizardService;
        _config = config.Value.Initialization;
        _logger = logger;

        DataContext = _viewModel;

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource?.Dispose();
        _cancellationTokenSource = null;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Create fresh CTS for each load
        _cancellationTokenSource?.Dispose();
        _cancellationTokenSource = new CancellationTokenSource();

        try
        {
            if (!await EnsureSetupAsync(_cancellationTokenSource.Token))
            {
                return;
            }

            await InitializeWithRetriesAsync(_viewModel, _cancellationTokenSource.Token);
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Critical error during application initialization");

            try
            {
                await _dialogService.ShowErrorAsync(
                    "A critical error occurred during application initialization that could not be recovered from.\n\n" +
                    $"Error details: {ex.Message}\n\n" +
                    "The application will now close. Please check the application logs for more details.",
                    "Critical Application Error");
            }
            catch (Exception dialogEx)
            {
                _logger.LogCritical(dialogEx, "Failed to show error dialog");
            }

            Application.Current.Exit();
        }
    }

    private async Task<bool> EnsureSetupAsync(CancellationToken cancellationToken)
    {
        var forceWizard = string.Equals(
            Environment.GetEnvironmentVariable("LIMOUNT_TEST_FORCE_WIZARD"),
            "1",
            StringComparison.OrdinalIgnoreCase);

        while (!cancellationToken.IsCancellationRequested)
        {
            var setupResult = await _setupWizardService.EnsureSetupAsync(forceWizard, cancellationToken);
            if (setupResult.IsCompleted)
            {
                return true;
            }

            var shouldExit = await _dialogService.ConfirmAsync(
                "Setup was canceled. LiMount requires setup to continue.\n\nDo you want to exit now?",
                "Setup Required",
                DialogType.Warning);

            if (shouldExit)
            {
                Application.Current.Exit();
                return false;
            }

            forceWizard = true;
        }

        return false;
    }

    private async Task InitializeWithRetriesAsync(MainViewModel viewModel, CancellationToken cancellationToken)
    {
        int retryCount = 0;
        bool extraRetryUsed = false;

        while (retryCount <= _config.MaxRetries && !cancellationToken.IsCancellationRequested)
        {
            try
            {
                await viewModel.InitializeAsync(cancellationToken);
                return;
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Initialization cancelled due to page unload");
                return;
            }
            catch (Exception ex)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    _logger.LogInformation("Initialization cancelled due to page unload");
                    return;
                }

                _logger.LogError(ex, "Failed to initialize ViewModel (attempt {Attempt}/{MaxAttempts})", retryCount + 1, _config.MaxRetries + 1);

                if (retryCount == _config.MaxRetries)
                {
                    var retry = await _dialogService.ConfirmAsync(
                        "Failed to initialize the application. This may be due to missing dependencies or insufficient permissions.\n\n" +
                        $"Error: {ex.Message}\n\n" +
                        "Would you like to retry one more time?",
                        "Initialization Failed",
                        DialogType.Error);

                    if (retry && !extraRetryUsed)
                    {
                        extraRetryUsed = true;
                        double delayMs = _config.BaseDelayMs * Math.Pow(2, retryCount);
                        double clampedDelay = Math.Max(0, Math.Min(delayMs, _config.MaxDelayMs));
                        await Task.Delay(TimeSpan.FromMilliseconds(clampedDelay), cancellationToken);
                        continue;
                    }

                    await _dialogService.ShowErrorAsync(
                        "The application could not be initialized and will now close. Please check the logs for details and ensure all required dependencies are available.",
                        "Application Cannot Start");
                    Application.Current.Exit();
                    return;
                }

                var retryNow = await _dialogService.ConfirmAsync(
                    $"Initialization failed (attempt {retryCount + 1} of {_config.MaxRetries + 1}).\n\n" +
                    $"Error: {ex.Message}\n\n" +
                    "Would you like to retry?",
                    "Initialization Failed",
                    DialogType.Warning);

                if (!retryNow)
                {
                    await _dialogService.ShowWarningAsync(
                        "Initialization aborted at user request. The application will close.",
                        "Initialization Aborted");
                    Application.Current.Exit();
                    return;
                }

                double retryDelayMs = _config.BaseDelayMs * Math.Pow(2, retryCount);
                double clampedRetryDelay = Math.Max(0, Math.Min(retryDelayMs, _config.MaxDelayMs));
                await Task.Delay(TimeSpan.FromMilliseconds(clampedRetryDelay), cancellationToken);
            }

            retryCount++;
        }
    }
}
