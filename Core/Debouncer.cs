namespace MostaqlK.Core;

/// <summary>
/// Shared cancel-and-restart debounce helper. Each <see cref="Schedule"/> call cancels any
/// pending invocation and restarts the delay; only the most recent schedule fires its action
/// once the quiet window elapses. Used by both UI (<c>DebouncedEntry</c>) and non-UI
/// (<c>ProjectFeedViewModel</c> auto-reload) call sites so neither hand-rolls its own
/// <see cref="CancellationTokenSource"/> restart mechanic.
/// </summary>
public sealed class Debouncer : IDisposable
{
    private TimeSpan _delay;
    private CancellationTokenSource? _cts;
    private bool _disposed;

    /// <summary>Creates a debouncer that waits <paramref name="delay"/> after the last schedule before running the action.</summary>
    public Debouncer(TimeSpan delay)
    {
        _delay = delay;
    }

    /// <summary>
    /// Updates the delay used by subsequent <see cref="Schedule"/> calls (e.g. when a bindable
    /// <c>DebounceMilliseconds</c> property changes). Does not affect an already-pending call.
    /// </summary>
    public void SetDelay(TimeSpan delay) => _delay = delay;

    /// <summary>
    /// Cancels any pending call and restarts the delay. When the quiet window elapses without
    /// another schedule, <paramref name="action"/> is invoked with the schedule's token.
    /// </summary>
    public void Schedule(Func<CancellationToken, Task> action)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(action);

        var cts = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _cts, cts);
        previous?.Cancel();
        previous?.Dispose();

        _ = RunAsync(action, cts.Token);
    }

    private async Task RunAsync(Func<CancellationToken, Task> action, CancellationToken token)
    {
        try
        {
            // Preserve the caller's synchronization context (e.g. UI thread for DebouncedEntry)
            // so event/command handlers run where the original hand-rolled debounce did.
            await Task.Delay(_delay, token);
        }
        catch (OperationCanceledException)
        {
            // Expected: a newer Schedule call restarted the debounce window and cancelled this one.
            return;
        }

        if (token.IsCancellationRequested)
        {
            return;
        }

        try
        {
            await action(token);
        }
        catch (OperationCanceledException)
        {
            // Action itself observed cancellation — treat as a no-op.
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        var cts = Interlocked.Exchange(ref _cts, null);
        cts?.Cancel();
        cts?.Dispose();
    }
}
