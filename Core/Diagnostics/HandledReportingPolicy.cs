namespace MostaqlK.Core.Diagnostics;

using System.Runtime.CompilerServices;

/// <summary>
/// Pure sampling rules for forwarding a CAUGHT exception to telemetry (the seam that makes a
/// swallowed throw visible outside the local log). Isolated (no Sentry, no MAUI, no IO) so the
/// crash-path tests can prove budget, suppression and dedupe without a network or an SDK.
/// <para>
/// Why this exists at all: the dev69 crash loop was invisible for 9 days because a composition
/// catch threw, swallowed, and called nothing. The local <c>FAULT</c> line is now unconditional
/// and first; this policy only gates the network side, so the worst outcome of any decision here
/// is "not forwarded yet", never "never happened".
/// </para>
/// </summary>
public enum HandledReportDecision
{
    /// <summary>Send now (fresh key within budget, or a due re-report with nothing suppressed since).</summary>
    Report,

    /// <summary>Send now and carry the count of occurrences suppressed since the last send.</summary>
    ReportWithSuppressed,

    /// <summary>Local log only; count it so a later re-report can prove how many were hidden.</summary>
    Suppress,

    /// <summary>This checkpoint never forwards — its sink or sender already owns a send.</summary>
    Exempt,
}

/// <summary>
/// Per-process budget for handled-exception forwarding, keyed by <c>checkpoint | exceptionType</c>.
/// Counting is per-process on purpose: a crash loop is many processes, and a fresh instance
/// re-reporting a live defect is correct — Sentry groups by stack, so re-reports collapse into
/// the same issue instead of spamming. The <see cref="PerProcessReportCeiling"/> exists because
/// the SDK's worker queue is bounded and overflow EVICTS queued items — an uncapped UI-thread
/// storm would drop exactly the fatal dump events the crash forwarder sends.
/// </summary>
public static class HandledReportingPolicy
{
    /// <summary>Fresh sends allowed per key before suppression starts.</summary>
    public const int InitialBudgetPerKey = 3;

    /// <summary>Quiet interval after which a still-occurring key re-reports once (with the suppressed count).</summary>
    public static readonly TimeSpan ReReportInterval = TimeSpan.FromMinutes(10);

    /// <summary>Hard cap on forwards per process, across all keys (queue-eviction protection).</summary>
    public const int PerProcessReportCeiling = 40;

    /// <summary>
    /// Checkpoints whose sink or caller already owns a telemetry send, or whose firehose must
    /// never reach the network: the first-chance probe logs every throw under the diag flag;
    /// the two unhandled paths send through <c>CrashReporter.Report</c> themselves.
    /// </summary>
    public static string[] ExemptCheckpoints { get; } =
    [
        "FirstChance",
        "AppDomain.UnhandledException",
        "TaskScheduler.UnobservedTaskException",
    ];

    private sealed class KeyState
    {
        public int Reported;
        public int SuppressedSinceLastSend;
        public DateTime LastReportUtc;
    }

    private static readonly object Gate = new();
    private static readonly Dictionary<string, KeyState> Keys = new(StringComparer.Ordinal);
    private static readonly ConditionalWeakTable<Exception, object> Forwarded = new();
    private static int _totalForwarded;

    public static bool IsExempt(string checkpoint) =>
        Array.Exists(ExemptCheckpoints, c => string.Equals(c, checkpoint, StringComparison.Ordinal));

    /// <summary>Decides whether this occurrence may be forwarded. Thread-safe; never throws.</summary>
    public static HandledReportDecision Decide(string checkpoint, Exception exception, DateTime utcNow)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        ArgumentNullException.ThrowIfNull(exception);

        if (IsExempt(checkpoint))
        {
            return HandledReportDecision.Exempt;
        }

        var key = checkpoint + "|" + exception.GetType().FullName;

        lock (Gate)
        {
            if (_totalForwarded >= PerProcessReportCeiling)
            {
                return HandledReportDecision.Suppress;
            }

            if (!Keys.TryGetValue(key, out var state))
            {
                state = new KeyState();
                Keys[key] = state;
            }

            if (state.Reported < InitialBudgetPerKey)
            {
                state.Reported++;
                state.LastReportUtc = utcNow;
                _totalForwarded++;
                return state.SuppressedSinceLastSend > 0
                    ? HandledReportDecision.ReportWithSuppressed
                    : HandledReportDecision.Report;
            }

            if (utcNow - state.LastReportUtc >= ReReportInterval)
            {
                state.Reported++;
                state.LastReportUtc = utcNow;
                _totalForwarded++;
                // The re-report carries the count; only ReportWithSuppressed when it actually hides something.
                return state.SuppressedSinceLastSend > 0
                    ? HandledReportDecision.ReportWithSuppressed
                    : HandledReportDecision.Report;
            }

            state.SuppressedSinceLastSend++;
            return HandledReportDecision.Suppress;
        }
    }

    /// <summary>Occurrences suppressed since the last forward for this key; also RESETS the counter (the event carries it).</summary>
    public static int TakeSuppressedCount(string checkpoint, Exception exception)
    {
        var key = checkpoint + "|" + exception.GetType().FullName;
        lock (Gate)
        {
            if (Keys.TryGetValue(key, out var state))
            {
                var n = state.SuppressedSinceLastSend;
                state.SuppressedSinceLastSend = 0;
                return n;
            }
        }

        return 0;
    }

    public static int TotalForwarded
    {
        get { lock (Gate) { return _totalForwarded; } }
    }

    /// <summary>
    /// Marks an exception object as already forwarded exactly once. Sites that call BOTH
    /// <c>CrashReporter.Report</c> (which sends) and <c>InteractionLogger.Fault</c> (whose observer
    /// would send again) produce one event, whichever ran first. Identity-based, so a new throw
    /// with the same message is a new candidate — Sentry's own grouping collapses true repeats.
    /// </summary>
    public static bool TryMarkExceptionForwarded(Exception exception)
    {
        lock (Gate)
        {
            if (Forwarded.TryGetValue(exception, out _))
            {
                return false;
            }

            Forwarded.Add(exception, new object());
            return true;
        }
    }

    /// <summary>Test isolation only; production state is per-process by design.</summary>
    public static void ResetForTests()
    {
        lock (Gate)
        {
            Keys.Clear();
            Forwarded.Clear(); // instance Clear() exists since .NET 9; we target net10 on every TFM
            _totalForwarded = 0;
        }
    }
}
