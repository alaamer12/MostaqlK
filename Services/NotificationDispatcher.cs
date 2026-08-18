using MostaqlK.Core;
using MostaqlK.Models;
using MostaqlK.Services.Diagnostics;

namespace MostaqlK.Services;

/// <inheritdoc cref="INotificationDispatcher"/>
public sealed class NotificationDispatcher : INotificationDispatcher
{
    private const int MaxHistory = 10;

    private readonly INotificationSender _toastSender;
    private readonly NotificationGrouper _grouper;
    private readonly GlobalAppStatusService _globalStatus;
    private readonly Lock _historyGate = new();
    private readonly List<ProjectSummary> _history = [];

    /// <summary>
    /// Bounded (last <see cref="MaxHistory"/>) in-memory history of dispatched notifications,
    /// newest first. Backs <c>NotificationCenterViewModel</c>'s recent-notifications flyout.
    /// No DB persistence per V1 scope — resets on app restart.
    /// </summary>
    public IReadOnlyList<ProjectSummary> RecentHistory
    {
        get
        {
            lock (_historyGate)
            {
                return _history.ToList();
            }
        }
    }

    /// <summary>Raised after a batch is added to <see cref="RecentHistory"/>, so the UI can refresh.</summary>
    public event Action? HistoryChanged;

    public NotificationDispatcher(INotificationSender toastSender, NotificationGrouper grouper, GlobalAppStatusService globalStatus)
    {
        _toastSender = toastSender;
        _grouper = grouper;
        _globalStatus = globalStatus;
        _grouper.OnFlush += HandleFlush;
    }

    public Task<Result<bool>> NotifyNewProjectsAsync(IReadOnlyList<ProjectSummary> projects, CancellationToken cancellationToken = default)
    {
        // Each project is fed into the grouper, which owns the timing/count thresholds and the
        // single-item bypass rule (system-components.md #12): whatever batch size it ends up
        // flushing with is what `WindowsToastSender` renders as either an individual or grouped
        // toast. Delivery itself happens asynchronously off `OnFlush`, so this always succeeds
        // as far as the caller is concerned — actual toast failures are reported/logged from
        // `HandleFlush`.
        foreach (var project in projects)
        {
            _grouper.Add(project);
        }

        // FIX (unread badge desync - e.g. mark-all-as-read resets it to 0, then a single new
        // project arrives but the badge jumps straight to 9): the badge used to be incremented
        // from HandleFlush, which only fires whenever the toast grouper decides to flush. The
        // default grouping mode (EndOfMinute, always enabled per SettingsViewModel.LoadFromPreferences
        // -> ApplyGroupingSettings) buffers every project that becomes unread over the course of a
        // whole minute before flushing them together. If the user marks everything as read before
        // that buffer flushes, the still-pending items were never counted toward the badge - so
        // when the timer eventually fires (possibly together with a brand-new project), the badge
        // jumps by the entire stale batch instead of by just the new item. A project becomes
        // unread the instant it's enriched, so the badge must count it right here, immediately,
        // independent of whenever its toast happens to be batched/shown.
        if (projects.Count > 0)
        {
            _globalStatus.IncrementUnreadNotificationCount(projects.Count);
        }

        return Task.FromResult(Result<bool>.Ok(true));
    }

    private void HandleFlush(IReadOnlyList<ProjectSummary> batch)
    {
        InteractionLogger.Mark("NotificationDispatcher.HandleFlush", "A", new { Count = batch.Count });

        lock (_historyGate)
        {
            // Newest first, capped at MaxHistory.
            _history.InsertRange(0, batch);
            if (_history.Count > MaxHistory)
            {
                _history.RemoveRange(MaxHistory, _history.Count - MaxHistory);
            }
        }

        // NOTE: the unread badge is now incremented from `NotifyNewProjectsAsync`, the instant a
        // project is queued for notification, not from here - `HandleFlush` only fires whenever
        // the toast grouper decides a batch is ready, which for the default EndOfMinute mode can
        // be up to a minute after the project actually became unread (see the FIX comment in
        // `NotifyNewProjectsAsync` for the full story on the "mark all as read -> 0 -> jumps to 9"
        // bug this decoupling caused). Incrementing here too would double-count every project.
        HistoryChanged?.Invoke();

        // Fire-and-forget: the flush originates from either a background timer callback or the
        // synchronous Add() call above, neither of which can usefully await the toast delivery.
        // WindowsToastSender already logs both success and (crucially) failure via
        // InteractionLogger, but the outcome is checked here too so a failed send is never
        // silently invisible even if that internal logging were ever bypassed.
        _ = _toastSender.SendAsync(batch).ContinueWith(task =>
        {
            if (task.IsCompletedSuccessfully && task.Result.IsError)
            {
                InteractionLogger.Mark("NotificationDispatcher.HandleFlush", "B", new { Reason = "toast-send-failed", Error = task.Result.Error?.ToString() });
            }
            else if (task.IsFaulted)
            {
                InteractionLogger.Fault("NotificationDispatcher.HandleFlush", task.Exception ?? new Exception("Unknown toast send failure"));
            }
        }, TaskScheduler.Default);
    }

    public void MarkHistoryAsRead()
    {
        lock (_historyGate)
        {
            foreach (var project in _history)
            {
                project.IsUnread = false;
            }
        }

        HistoryChanged?.Invoke();
    }
}
