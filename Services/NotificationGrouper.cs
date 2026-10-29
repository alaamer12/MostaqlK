using System.Linq;
using MostaqlK.Core.Sources;
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
/// <para>
/// The buffer is bucketed by source, so every batch handed to <see cref="OnFlush"/> — and therefore
/// every grouped toast — has exactly one producing platform. That is a consequence of the toast now
/// wearing that platform's colors: a batch mixing two sources could only dress itself in one of them
/// and misreport the other. The cost is more toasts when several platforms discover work in the same
/// window, which the owner accepted deliberately.
/// </para>
/// </summary>
public sealed class NotificationGrouper : IDisposable
{
    private readonly Lock _gate = new();
    private readonly Dictionary<SourceId, List<ProjectSummary>> _buckets = [];
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
    /// Raised once per source whenever that source's batch is ready to be sent as a toast — either
    /// because a timing/count threshold was reached, or (per the single-item bypass rule) because the
    /// batch that would flush contains exactly one item.
    /// </summary>
    public event Action<IReadOnlyList<ProjectSummary>>? OnFlush;

    /// <summary>
    /// Buffers <paramref name="project"/> into its source's bucket and schedules/triggers a flush
    /// according to <see cref="Mode"/>. Mirrors system-components.md #12 and
    /// configuration-reference.md § Notification grouping.
    /// </summary>
    public void Add(ProjectSummary project)
    {
        List<IReadOnlyList<ProjectSummary>> readyBatches = [];

        lock (_gate)
        {
            if (!_buckets.TryGetValue(project.Source, out var bucket))
            {
                bucket = [];
                _buckets[project.Source] = bucket;
            }

            bucket.Add(project);

            if (!Enabled)
            {
                readyBatches = DrainAllLocked();
            }
            else if (Mode == NotificationGroupingMode.AfterCount && bucket.Count >= AfterCountThreshold)
            {
                // Only the bucket that tripped the threshold ships. The other platforms keep
                // buffering toward their own toast, because "5 new projects" means five from one
                // source, not five across the whole feed.
                readyBatches.Add(DrainBucketLocked(project.Source));
            }
            else if (_flushTimer is null)
            {
                var dueTime = DueTimeFor(Mode);
                _flushTimer = new Timer(_ => FlushDue(), null, dueTime, Timeout.InfiniteTimeSpan);
                InteractionLogger.Mark("NotificationGrouper.Add", "A", new { Mode = Mode.ToString(), DueTimeSeconds = dueTime.TotalSeconds });
            }
        }

        foreach (var batch in readyBatches)
        {
            if (batch.Count == 0)
            {
                continue;
            }

            InteractionLogger.Mark("NotificationGrouper.Flush", "A", new { Reason = "immediate", Source = batch[0].Source.Value, Count = batch.Count });
            OnFlush?.Invoke(batch);
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
        List<IReadOnlyList<ProjectSummary>> batches;

        lock (_gate)
        {
            batches = DrainAllLocked();
        }

        var flushed = 0;
        foreach (var batch in batches)
        {
            if (batch.Count == 0)
            {
                continue;
            }

            flushed += batch.Count;
            InteractionLogger.Mark("NotificationGrouper.Flush", "A", new { Reason = "timer-or-force", Source = batch[0].Source.Value, Count = batch.Count });
            OnFlush?.Invoke(batch);
        }

        if (flushed == 0)
        {
            InteractionLogger.Mark("NotificationGrouper.Flush", "B", "nothing-pending");
        }
    }

    /// <summary>
    /// Empties one source's bucket. Must be called while holding <see cref="_gate"/>. The flush timer
    /// stays armed while any other bucket still holds work, so draining one platform never cancels
    /// another platform's scheduled toast.
    /// </summary>
    private IReadOnlyList<ProjectSummary> DrainBucketLocked(SourceId source)
    {
        if (!_buckets.TryGetValue(source, out var bucket) || bucket.Count == 0)
        {
            return [];
        }

        var batch = bucket.ToArray();
        bucket.Clear();
        _buckets.Remove(source);

        if (_buckets.Count == 0)
        {
            _flushTimer?.Dispose();
            _flushTimer = null;
        }

        return batch;
    }

    /// <summary>
    /// Empties every bucket, one batch per source. Must be called while holding <see cref="_gate"/>.
    /// Oldest item first, then by source id, so the order toasts appear in is stable.
    /// </summary>
    private List<IReadOnlyList<ProjectSummary>> DrainAllLocked()
    {
        _flushTimer?.Dispose();
        _flushTimer = null;

        if (_buckets.Count == 0)
        {
            return [];
        }

        var batches = _buckets
            .OrderBy(entry => FirstSeen(entry.Value))
            .ThenBy(entry => entry.Key.Value)
            .Select(entry => (IReadOnlyList<ProjectSummary>)entry.Value.ToArray())
            .ToList();

        _buckets.Clear();
        return batches;
    }

    /// <summary>
    /// Bucket ordering key: the moment the bucket first became non-empty. Ties are broken by source id
    /// in the caller, so the order toasts appear in is the same every time.
    /// </summary>
    private static long FirstSeen(IReadOnlyList<ProjectSummary> bucket) => bucket[0].PublishTimeNumber;

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
