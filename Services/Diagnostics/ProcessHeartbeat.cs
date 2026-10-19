using System.Diagnostics;
using MostaqlK.Core.Platform;

namespace MostaqlK.Services.Diagnostics;

/// <summary>
/// Periodic process-health line written to <c>log/&lt;date&gt;/heartbeat.log</c>.
/// <para>
/// Before this existed, a process that died silently in the tray left no trace of <i>when</i> it died
/// or what state it was in: the interaction log only records events (a quiet poll cycle writes nothing),
/// so the 2026-10-19 crash at 13:49 looked like "last log 13:21". The last heartbeat now brackets the
/// time of death to within one interval, and the trend of finalizer backlog / GC counts / handles /
/// threads / memory shows whether native-object churn (the root cause of that crash) is back.
/// </para>
/// </summary>
public static class ProcessHeartbeat
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(2);

    private static Timer? _timer;
    private static Func<string>? _stateProvider;
    private static readonly DateTime StartedAt = DateTime.Now;
    private static long _beat;

    /// <summary>
    /// Starts the heartbeat (idempotent). <paramref name="stateProvider"/> adds app-level state to every
    /// line (e.g. background/foreground) and must be cheap and thread-safe.
    /// </summary>
    public static void Start(Func<string>? stateProvider = null)
    {
        _stateProvider = stateProvider;
        if (_timer is not null)
        {
            return;
        }

        Write("start");
        _timer = new Timer(_ => Write("beat"), null, Interval, Interval);
    }

    /// <summary>Writes one immediate line tagged with <paramref name="reason"/> (e.g. a lifecycle transition).</summary>
    public static void Mark(string reason) => Write(reason);

    private static void Write(string reason)
    {
        try
        {
            using var process = Process.GetCurrentProcess();
            var gc = GC.GetGCMemoryInfo();
            string state;
            try
            {
                state = _stateProvider?.Invoke() ?? "-";
            }
            catch (Exception ex)
            {
                state = "state-error:" + ex.GetType().Name;
            }

            var line =
                FormattableString.Invariant($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] #{Interlocked.Increment(ref _beat)} {reason} ") +
                FormattableString.Invariant($"uptime={DateTime.Now - StartedAt:d\\.hh\\:mm\\:ss} {state} ") +
                FormattableString.Invariant($"ws={process.WorkingSet64 / 1048576}MB private={process.PrivateMemorySize64 / 1048576}MB ") +
                FormattableString.Invariant($"managed={GC.GetTotalMemory(false) / 1048576}MB gen0={GC.CollectionCount(0)} gen1={GC.CollectionCount(1)} gen2={GC.CollectionCount(2)} ") +
                FormattableString.Invariant($"finalizePending={gc.FinalizationPendingCount} pinned={gc.PinnedObjectsCount} ") +
                FormattableString.Invariant($"handles={process.HandleCount} threads={process.Threads.Count}") + Environment.NewLine;

            File.AppendAllText(Path.Combine(AppPaths.LogsDirectory, "heartbeat.log"), line);
        }
        catch
        {
            // Diagnostics must never become the crash.
        }
    }
}
