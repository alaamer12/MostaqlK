using MostaqlK.Core;
using MostaqlK.Infrastructure.Notifications;
using MostaqlK.Models;
using MostaqlK.Services.Diagnostics;

namespace MostaqlK.Services;

/// <inheritdoc cref="INotificationDispatcher"/>
public sealed class NotificationDispatcher : INotificationDispatcher
{
    private const int MaxHistory = 10;

    private readonly WindowsToastSender _toastSender;
    private readonly NotificationGrouper _grouper;
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

    public NotificationDispatcher(WindowsToastSender toastSender, NotificationGrouper grouper)
    {
        _toastSender = toastSender;
        _grouper = grouper;
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
}
