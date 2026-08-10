using MostaqlK.Models;
using MostaqlK.Services.Diagnostics;

namespace MostaqlK.Services;

/// <summary>
/// Strategy used to batch newly enriched projects into a single toast rather than
/// firing one notification per project.
/// </summary>
public enum NotificationGroupingMode
{
    /// <summary>Flush whatever accumulated at the end of the current minute.</summary>
    EndOfMinute,

    /// <summary>Flush after a configurable number of minutes has elapsed since the first item.</summary>
    AfterMinutes,

    /// <summary>Flush as soon as a configurable item count is reached.</summary>
    AfterCount
}

/// <summary>
/// Buffers newly discovered/enriched projects and decides when a batch is ready to be
/// flushed to the <see cref="INotificationDispatcher"/> based on the configured grouping mode.
/// </summary>
public sealed class NotificationGrouper : IDisposable
{
    private readonly Lock _gate = new();
    private readonly List<ProjectSummary> _pending = [];
    private Timer? _flushTimer;

    /// <summary>
    /// Mirrors `notification_grouping_enabled` (default: false). While disabled, every project
    /// flushes immediately as its own individual toast, bypassing timing/count thresholds entirely.
    /// </summary>
    public bool Enabled { get; set; }

    public NotificationGroupingMode Mode { get; set; } = NotificationGroupingMode.EndOfMinute;

    public int AfterMinutesThreshold { get; set; } = 1;

    public int AfterCountThreshold { get; set; } = 5;

    /// <summary>
    /// Raised whenever a batch is ready to be sent as a toast — either because a timing/count
    /// threshold was reached, or (per the single-item bypass rule) because the batch that would
    /// flush contains exactly one item.
    /// </summary>
    public event Action<IReadOnlyList<ProjectSummary>>? OnFlush;

    /// <summary>
    /// Buffers <paramref name="project"/> and schedules/triggers a flush according to
    /// <see cref="Mode"/>. Mirrors system-components.md #12 and
    /// configuration-reference.md § Notification grouping.
    /// </summary>
    public void Add(ProjectSummary project)
    {
        IReadOnlyList<ProjectSummary>? readyBatch = null;

        lock (_gate)
        {
            _pending.Add(project);

            if (!Enabled || (Mode == NotificationGroupingMode.AfterCount && _pending.Count >= AfterCountThreshold))
            {
                readyBatch = DrainLocked();
            }
            else if (_flushTimer is null)
            {
                var dueTime = DueTimeFor(Mode);
                _flushTimer = new Timer(_ => FlushDue(), null, dueTime, Timeout.InfiniteTimeSpan);
                InteractionLogger.Mark("NotificationGrouper.Add", "A", new { Mode = Mode.ToString(), DueTimeSeconds = dueTime.TotalSeconds });
            }
        }

        if (readyBatch is { Count: > 0 })
        {
            InteractionLogger.Mark("NotificationGrouper.Flush", "A", new { Reason = "immediate", Count = readyBatch.Count });
            OnFlush?.Invoke(readyBatch);
        }
    }

    /// <summary>
    /// Forces whatever is currently pending to flush immediately, regardless of mode/threshold.
    /// Intended for shutdown paths so nothing buffered is silently dropped.
    /// </summary>
    public void FlushNow()
    {
        FlushDue();
    }

    private void FlushDue()
    {
        IReadOnlyList<ProjectSummary>? batch;

        lock (_gate)
        {
            batch = DrainLocked();
        }

        if (batch is { Count: > 0 })
        {
            InteractionLogger.Mark("NotificationGrouper.Flush", "A", new { Reason = "timer-or-force", Count = batch.Count });
            OnFlush?.Invoke(batch);
        }
        else
        {
            InteractionLogger.Mark("NotificationGrouper.Flush", "B", "nothing-pending");
        }
    }

    /// <summary>
    /// Clears and returns the pending batch. Must be called while holding <see cref="_gate"/>.
    /// </summary>
    private IReadOnlyList<ProjectSummary> DrainLocked()
    {
        _flushTimer?.Dispose();
        _flushTimer = null;

        if (_pending.Count == 0)
        {
            return [];
        }

        var batch = _pending.ToList();
        _pending.Clear();
        return batch;
    }

    private TimeSpan DueTimeFor(NotificationGroupingMode mode) => mode switch
    {
        NotificationGroupingMode.EndOfMinute => TimeUntilNextMinuteBoundary(),
        NotificationGroupingMode.AfterMinutes => TimeSpan.FromMinutes(Math.Max(1, AfterMinutesThreshold)),
        // AfterCount has no timer — it flushes eagerly in Add() once the threshold is hit. If the
        // threshold is never reached, fall back to a generous safety-net flush so nothing is
        // buffered forever.
        NotificationGroupingMode.AfterCount => TimeSpan.FromMinutes(5),
        _ => TimeUntilNextMinuteBoundary(),
    };

    private static TimeSpan TimeUntilNextMinuteBoundary()
    {
        var now = DateTimeOffset.UtcNow;
        var nextMinute = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0, now.Offset)
            .AddMinutes(1);
        var delay = nextMinute - now;
        return delay > TimeSpan.Zero ? delay : TimeSpan.FromSeconds(1);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _flushTimer?.Dispose();
            _flushTimer = null;
        }
    }
}
