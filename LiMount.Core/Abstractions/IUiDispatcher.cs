namespace LiMount.Core.Abstractions;

/// <summary>
/// Platform-agnostic interface for dispatching work to the UI thread.
/// Implementations should wrap the platform-specific dispatcher (WPF Dispatcher, WinUI DispatcherQueue).
/// </summary>
public interface IUiDispatcher
{
    /// <summary>
    /// Gets whether the current thread has access to the UI thread.
    /// </summary>
    bool HasThreadAccess { get; }

    /// <summary>
    /// Executes the specified asynchronous action on the UI thread.
    /// If already on the UI thread, executes immediately.
    /// </summary>
    /// <param name="action">The async action to execute.</param>
    /// <returns>A task that completes when the action has finished.</returns>
    Task RunAsync(Func<Task> action);

    /// <summary>
    /// Enqueues the specified action to run on the UI thread.
    /// Does not wait for completion.
    /// </summary>
    /// <param name="action">The action to enqueue.</param>
    void Enqueue(Action action);
}
