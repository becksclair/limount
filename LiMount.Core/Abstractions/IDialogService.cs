namespace LiMount.Core.Abstractions;

/// <summary>
/// Platform-agnostic interface for showing dialogs to the user.
/// Implementations should use the platform-specific dialog mechanisms (MessageBox, ContentDialog, etc.).
/// </summary>
public interface IDialogService
{
    /// <summary>
    /// Shows a confirmation dialog with Yes/No options.
    /// </summary>
    /// <param name="message">The message to display.</param>
    /// <param name="title">The dialog title.</param>
    /// <param name="type">The type of dialog (affects styling).</param>
    /// <returns>True if the user confirmed, false otherwise.</returns>
    Task<bool> ConfirmAsync(string message, string title, DialogType type = DialogType.Warning);

    /// <summary>
    /// Shows an error dialog.
    /// </summary>
    /// <param name="message">The error message to display.</param>
    /// <param name="title">The dialog title.</param>
    Task ShowErrorAsync(string message, string title = "Error");

    /// <summary>
    /// Shows an informational dialog.
    /// </summary>
    /// <param name="message">The message to display.</param>
    /// <param name="title">The dialog title.</param>
    Task ShowInfoAsync(string message, string title = "Information");

    /// <summary>
    /// Shows a warning dialog.
    /// </summary>
    /// <param name="message">The warning message to display.</param>
    /// <param name="title">The dialog title.</param>
    Task ShowWarningAsync(string message, string title = "Warning");
}

/// <summary>
/// Specifies the type of dialog to display.
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
