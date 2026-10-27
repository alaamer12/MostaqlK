using MostaqlK.Core;
using MostaqlK.Core.Sources;
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
    private readonly ISettingsRepository _settings;
    private readonly PlatformEnablement _enablement;
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

    public NotificationDispatcher(
        INotificationSender toastSender,
        NotificationGrouper grouper,
        GlobalAppStatusService globalStatus,
        ISettingsRepository settings,
        PlatformEnablement enablement)
    {
        _toastSender = toastSender;
        _grouper = grouper;
        _globalStatus = globalStatus;
        _settings = settings;
        _enablement = enablement;
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
        // "تنبيهات هذه المنصة" is enforced HERE. The row existed and persisted since the multi-source
        // work, but nothing ever read it — flipping it off changed a number on disk while every toast
        // still arrived. Two independent authorities are asked, never merged: that per-platform
        // toggle, and the platform being switched off at all (a disabled platform emits nothing
        // whatever its toggle says). Both gate the TOAST BUFFER ONLY: the unread badge and the flyout
        // history below keep counting, because silencing a platform's popups must not hide its projects.
        var mutedSources = projects
            .Select(project => project.Source)
            .Distinct()
            .Where(source => _settings.IsMuted(source) || !_enablement.IsEnabled(source))
            .ToList();

        foreach (var project in projects)
        {
            if (mutedSources.Contains(project.Source))
            {
                continue;
            }

            _grouper.Add(project);
        }

        if (mutedSources.Count > 0)
        {
            InteractionLogger.Mark("NotificationDispatcher.NotifyNewProjectsAsync", "B", new
            {
                Reason = "platform-notifications-muted",
                Muted = string.Join(",", mutedSources.Select(source => source.Value)),
                Count = projects.Count
            });
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
        //
        // The flyout's history is written in the SAME place, for the same reason: it used to be
        // appended only on the grouper's flush, so opening the bell during the EndOfMinute window
        // showed a non-zero badge over an EMPTY recent list (the exact reported "empty dropdown").
        if (projects.Count > 0)
        {
            lock (_historyGate)
            {
                // Newest first, capped at MaxHistory (same ordering the flush used to provide).
                _history.InsertRange(0, projects);
                if (_history.Count > MaxHistory)
                {
                    _history.RemoveRange(MaxHistory, _history.Count - MaxHistory);
                }
            }

            _globalStatus.IncrementUnreadNotificationCount(projects.Count);
            HistoryChanged?.Invoke();
        }

        return Task.FromResult(Result<bool>.Ok(true));
    }

    private void HandleFlush(IReadOnlyList<ProjectSummary> batch)
    {
        InteractionLogger.Mark("NotificationDispatcher.HandleFlush", "A", new { Count = batch.Count });

        // History + unread badge are written by NotifyNewProjectsAsync the instant a project
        // becomes unread (see the FIX comment there); this handler owns toast delivery only.
        // Writing them here as well would double-count every flushed project.

        // Fire-and-forget: the flush originates from either a background timer callback or the
        // synchronous Add() call above, neither of which can usefully await the toast delivery.
        // WindowsToastSender already logs both success and (crucially) failure via
        // InteractionLogger, but the outcome is checked here too so a failed send is never
        // silently invisible even if that internal logging were ever bypassed.
        //
        // The call itself is guarded because `SendAsync` is synchronous up to the point it
        // returns a Task: a WinRT failure (0x80004002 from the toast interop on some desktop
        // layouts) throws on THIS thread, before any continuation can observe it, and an
        // unobserved throw on the UI thread is a process death. Notifications are a nice-to-have;
        // they must never be able to take the app down.
        try
        {
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
        catch (Exception ex)
        {
            InteractionLogger.Fault("NotificationDispatcher.HandleFlush", ex);
        }
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
