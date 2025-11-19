namespace LiMount.App.Services;

/// <summary>
/// Service for displaying dialogs to the user.
/// Abstracts UI dialog implementation to make ViewModels testable.
/// </summary>
public interface IDialogService
{
    /// <summary>
    /// Shows a confirmation dialog asking the user to confirm an action.
    /// </summary>
    /// <param name="message">The message to display.</param>
    /// <param name="title">The dialog title.</param>
    /// <param name="dialogType">The type of dialog (Info, Warning, Error).</param>
    /// <returns>True if user confirmed, false if user cancelled.</returns>
    Task<bool> ConfirmAsync(string message, string title, DialogType dialogType = DialogType.Warning);

    /// <summary>
    /// Shows an error message to the user.
    /// </summary>
    /// <param name="message">The error message to display.</param>
    /// <param name="title">The dialog title.</param>
    Task ShowErrorAsync(string message, string title = "Error");

    /// <summary>
    /// Shows an informational message to the user.
    /// </summary>
    /// <param name="message">The information message to display.</param>
    /// <param name="title">The dialog title.</param>
    Task ShowInfoAsync(string message, string title = "Information");

    /// <summary>
    /// Shows a warning message to the user.
    /// </summary>
    /// <param name="message">The warning message to display.</param>
    /// <param name="title">The dialog title.</param>
    Task ShowWarningAsync(string message, string title = "Warning");
}

/// <summary>
/// Type of dialog to display.
/// </summary>
public enum DialogType
{
    /// <summary>
    /// Informational dialog.
    /// </summary>
    Information,

    /// <summary>
    /// Warning dialog.
    /// </summary>
    Warning,

    /// <summary>
    /// Error dialog.
    /// </summary>
    Error
}
