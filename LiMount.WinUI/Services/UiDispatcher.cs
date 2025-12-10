using Microsoft.UI.Dispatching;
using LiMount.Core.Abstractions;

namespace LiMount.WinUI.Services;

/// <summary>
/// WinUI implementation of IUiDispatcher using DispatcherQueue.
/// Requires initialization with the DispatcherQueue before use.
/// </summary>
public sealed class UiDispatcher : IUiDispatcher
{
    private DispatcherQueue? _dispatcherQueue;
    private bool _isInitialized;

    /// <summary>
    /// Initializes the dispatcher with the WinUI DispatcherQueue.
    /// Must be called from the UI thread before any other methods.
    /// </summary>
    public void Initialize(DispatcherQueue dispatcherQueue)
    {
        _dispatcherQueue = dispatcherQueue ?? throw new ArgumentNullException(nameof(dispatcherQueue));
        _isInitialized = true;
    }

    public bool HasThreadAccess => _dispatcherQueue?.HasThreadAccess ?? false;

    public void Enqueue(Action action)
    {
        if (!_isInitialized)
        {
            throw new InvalidOperationException(
                "UiDispatcher not initialized. Call Initialize() with DispatcherQueue before use.");
        }

        if (_dispatcherQueue!.HasThreadAccess)
        {
            action();
            return;
        }

        if (!_dispatcherQueue.TryEnqueue(() => action()))
        {
            throw new InvalidOperationException(
                "Dispatcher queue is not accepting new work. The application may be shutting down.");
        }
    }

    public Task RunAsync(Func<Task> action)
    {
        if (!_isInitialized)
        {
            throw new InvalidOperationException(
                "UiDispatcher not initialized. Call Initialize() with DispatcherQueue before use.");
        }

        if (_dispatcherQueue!.HasThreadAccess)
        {
            return action();
        }

        var tcs = new TaskCompletionSource();

        // TryEnqueue returns false when the dispatcher queue is shutting down.
        // If we don't handle this, the TaskCompletionSource will never complete,
        // causing any code awaiting tcs.Task to hang forever.
        if (!_dispatcherQueue.TryEnqueue(async () =>
        {
            try
            {
                await action();
                tcs.TrySetResult();
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        }))
        {
            tcs.TrySetException(new InvalidOperationException(
                "Dispatcher queue is not accepting new work. The application may be shutting down."));
        }

        return tcs.Task;
    }
}
