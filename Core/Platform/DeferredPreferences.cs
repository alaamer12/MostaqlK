using MostaqlK.Core.Sources;

namespace MostaqlK.Core.Platform;

/// <summary>
/// Frame-decoupled persistence for preference/settings writes (v3 plan Steps 2–3): keystroke-rate
/// settings edits, filter applies and mark-read DB writes must NEVER run their persistence
/// synchronously on the UI/click thread. Writes go into an ordered LAST-WINS queue keyed by a
/// caller-chosen string: a newer value for the same key replaces the still-pending one (the queue
/// never persists a superseded intermediate), and execution is fire-and-forget on a background
/// worker task — one at a time, FIFO across distinct keys in request order.
/// <para>
/// <b>Thread-safety assumption (documented):</b> the persistence targets are MAUI
/// <see cref="Microsoft.Maui.Storage.IPreferences"/> (backed by <c>FilePreferences</c>, whose own
/// operations are lock-guarded) and the <see cref="ISettingsRepository"/> over it — both are safe
/// to call from a single background worker thread. Repository reads (<c>Get</c>) keep hitting the
/// in-memory layer on the UI thread and are cheap; only the WRITE is deferred here.
/// </para>
/// <para>
/// <b>Teardown:</b> <see cref="FlushAsync"/> drains the queue and awaits the in-flight write; the
/// app must call it from the window-closing path so a pending preference never dies with the
/// process. A failed write is reported to the <see cref="FaultSink"/> and skipped (the queue
/// keeps draining) — a lost settings write is a cosmetic regression, a blocked UI thread is not.
/// </para>
/// </summary>
public static class DeferredPreferences
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, Func<Task>> Pending = [];
    private static readonly Queue<string> Order = [];
    private static Task? _currentWrite;
    private static Task? _drainLoop;

    /// <summary>Optional fault reporter (wired by the app to <c>InteractionLogger.Fault</c>); tests leave it null.</summary>
    public static Action<string, Exception?>? FaultSink { get; set; }

    /// <summary>
    /// Defers one async write under <paramref name="key"/>. A pending write with the same key is
    /// REPLACED (last-wins); distinct keys keep request order. Fire-and-forget — never awaited by
    /// the click path.
    /// </summary>
    public static void Enqueue(string key, Func<Task> write)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(write);

        lock (Gate)
        {
            if (!Pending.ContainsKey(key))
            {
                Order.Enqueue(key);
            }

            Pending[key] = write;

            _drainLoop ??= Task.Run(DrainAsync);
        }
    }

    /// <summary>Fire-and-forget synchronous-body write (preferences sets are sync calls behind the seam).</summary>
    public static void Enqueue(string key, Action write)
    {
        ArgumentNullException.ThrowIfNull(write);
        Enqueue(key, () =>
        {
            write();
            return Task.CompletedTask;
        });
    }

    /// <summary>Defers <see cref="ISettingsRepository.Set{T}"/> for one source; key = source + setting key, so repeated keystrokes collapse to the newest value.</summary>
    public static void Set<T>(ISettingsRepository settings, SourceId source, string key, T value)
    {
        Enqueue($"settings:{source.Value}:{key}", () => settings.Set(source, key, value));
    }

    /// <summary>Defers <see cref="ISettingsRepository.SetGlobal{T}"/>; key = setting key.</summary>
    public static void SetGlobal<T>(ISettingsRepository settings, string key, T value)
    {
        Enqueue($"settings:global:{key}", () => settings.SetGlobal(key, value));
    }

    /// <summary>
    /// Drains everything queued and awaits the in-flight write. The app teardown (window closing)
    /// MUST await this so pending preferences land before exit; the explicit Settings "Save" also
    /// calls it to honour "confirm nothing is pending".
    /// </summary>
    public static async Task FlushAsync()
    {
        while (true)
        {
            Task? inFlight;
            bool empty;
            lock (Gate)
            {
                empty = Pending.Count == 0;
                inFlight = _currentWrite;
            }

            if (empty && inFlight is null)
            {
                return;
            }

            if (inFlight is not null)
            {
                try
                {
                    await inFlight.ConfigureAwait(false);
                }
                catch
                {
                    // The drain loop already reported the fault; flush never rethrows.
                }

                continue;
            }

            // Queued but not yet picked up by the drain worker — short poll (teardown path, not a frame path).
            await Task.Delay(10).ConfigureAwait(false);
        }
    }

    private static async Task DrainAsync()
    {
        while (true)
        {
            Func<Task>? work = null;
            lock (Gate)
            {
                while (Order.Count > 0)
                {
                    var key = Order.Dequeue();
                    if (Pending.TryGetValue(key, out var pending))
                    {
                        Pending.Remove(key);
                        work = pending;
                        break;
                    }
                }

                if (work is null)
                {
                    _drainLoop = null;
                    return;
                }

                _currentWrite = InvokeAsync(work);
            }

            await _currentWrite.ConfigureAwait(false);
        }

        async Task InvokeAsync(Func<Task> write)
        {
            try
            {
                await write().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                FaultSink?.Invoke("DeferredPreferences", ex);
            }
            finally
            {
                lock (Gate)
                {
                    _currentWrite = null;
                    Monitor.PulseAll(Gate);
                }
            }
        }
    }
}
