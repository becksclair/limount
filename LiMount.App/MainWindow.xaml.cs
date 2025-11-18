using System.Windows;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using LiMount.App.ViewModels;
using LiMount.App.Services;
using LiMount.Core.Configuration;

namespace LiMount.App;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private readonly ILogger<MainWindow> _logger;
    private readonly IDialogService _dialogService;
    private readonly InitializationConfig _config;
    private readonly CancellationTokenSource _cancellationTokenSource = new();

    /// <summary>
    /// Initializes the window, assigns the provided view model to DataContext, and registers an asynchronous Loaded handler that initializes the view model; initialization failures are handled by showing a critical error and closing the window.
    /// </summary>
    /// <param name="viewModel">The view model instance to attach to the window as its DataContext and to initialize on load.</param>
    /// <param name="dialogService">Service for displaying dialogs to the user.</param>
    /// <param name="config">Configuration for application initialization behavior.</param>
    /// <param name="logger">The logger instance for logging initialization events and errors.</param>
    public MainWindow(
        MainViewModel viewModel,
        IDialogService dialogService,
        IOptions<LiMountConfiguration> config,
        ILogger<MainWindow> logger)
    {
        InitializeComponent();

        _logger = logger;
        _dialogService = dialogService;
        _config = config.Value.Initialization;

        // Set the DataContext to injected ViewModel
        DataContext = viewModel;

        // Subscribe to Loaded event for async initialization
        Loaded += async (sender, e) =>
        {
            try
            {
                await InitializeViewModelAsync(viewModel, _cancellationTokenSource.Token);
            }
            catch (Exception ex)
            {
                _logger.LogCritical(ex, "Unhandled exception in MainWindow Loaded event during ViewModel initialization");

                ShowCriticalErrorAndClose(ex);
            }
        };

        // Subscribe to Closed event for proper cleanup
        Closed += (sender, e) =>
        {
            _cancellationTokenSource.Cancel();
            _cancellationTokenSource.Dispose();
        };

        Closing += (sender, e) => _cancellationTokenSource.Cancel();
    }

    /// <summary>
    /// Initializes the provided MainViewModel with retry logic and exponential backoff; on repeated failures shows retry prompts and disables the UI if initialization cannot complete.
    /// </summary>
    /// <param name="viewModel">The MainViewModel to initialize; on failure this method may display dialogs prompting the user to retry and may disable the UI if initialization fails permanently.</param>
    /// <param name="cancellationToken">Token to cancel initialization if the window closes.</param>
    private async Task InitializeViewModelAsync(MainViewModel viewModel, CancellationToken cancellationToken)
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
                _logger.LogInformation("Initialization cancelled due to window closure");
                return;
            }
            catch (Exception ex)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    _logger.LogInformation("Initialization cancelled due to window closure");
                    return;
                }

                _logger.LogError(ex, "Failed to initialize ViewModel (attempt {Attempt}/{MaxAttempts})", retryCount + 1, _config.MaxRetries + 1);

                if (retryCount == _config.MaxRetries)
                {
                    // Final attempt failed - show error dialog and disable UI
                    if (cancellationToken.IsCancellationRequested)
                    {
                        return;
                    }

                    var retry = await _dialogService.ConfirmAsync(
                        "Failed to initialize the application. This may be due to missing dependencies or insufficient permissions.\n\n" +
                        $"Error: {ex.Message}\n\n" +
                        "Would you like to retry one more time?",
                        "Initialization Failed",
                        DialogType.Error);

                    if (retry && !extraRetryUsed)
                    {
                        extraRetryUsed = true;
                        // Apply exponential backoff delay before final retry
                        var delay = Math.Min(_config.BaseDelayMs * (int)Math.Pow(2, retryCount), _config.MaxDelayMs);
                        await Task.Delay(delay, cancellationToken);
                        continue;
                    }

                    // User chose not to retry or extra retry already used
                    DisableUI();
                    return;
                }

                // Show retry dialog for non-final attempts
                if (cancellationToken.IsCancellationRequested)
                {
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
                    DisableUI();
                    return;
                }

                // Apply exponential backoff delay before retry
                var retryDelay = Math.Min(_config.BaseDelayMs * (int)Math.Pow(2, retryCount), _config.MaxDelayMs);
                await Task.Delay(retryDelay, cancellationToken);
            }

            retryCount++;
        }
    }

    /// <summary>
    /// Disables the window UI, presents a permanent startup-failure dialog, and closes the window.
    /// </summary>
    /// <remarks>
    /// Logs a warning and ensures the disable-and-close sequence runs on the UI thread.
    /// </remarks>
    private void DisableUI()
    {
        _logger.LogWarning("Application UI disabled due to initialization failure");

        if (Dispatcher.CheckAccess())
        {
            DisableUIInternal();
        }
        else
        {
            Dispatcher.Invoke(DisableUIInternal);
        }
    }

    /// <summary>
    /// Logs a critical initialization error, displays an error dialog with the exception message, and closes the window.
    /// </summary>
    /// <param name="ex">The exception that caused initialization to fail; its message is shown in the dialog.</param>
    private async void ShowCriticalErrorAndClose(Exception ex)
    {
        _logger.LogCritical(ex, "Critical error during application initialization - showing error dialog and closing");

        await _dialogService.ShowErrorAsync(
            "A critical error occurred during application initialization that could not be recovered from.\n\n" +
            $"Error details: {ex.Message}\n\n" +
            "The application will now close. Please check the application logs for more details.",
            "Critical Application Error");

        Close();
    }

    /// <summary>
    /// Disables the window's main content, shows a permanent error dialog indicating startup failure, and closes the window.
    /// </summary>
    private async void DisableUIInternal()
    {
        // Find the main grid or container and disable it
        if (Content is FrameworkElement element)
        {
            element.IsEnabled = false;
        }

        // Show a permanent error message
        await _dialogService.ShowErrorAsync(
            "The application could not be initialized and will now close. Please check the logs for details and ensure all required dependencies are available.",
            "Application Cannot Start");

        Close();
    }
}