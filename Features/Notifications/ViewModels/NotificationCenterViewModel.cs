using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MostaqlK.Core.Navigation;
using MostaqlK.Features.Projects.Views;
using MostaqlK.Models;
using MostaqlK.Services;

namespace MostaqlK.Features.Notifications.ViewModels;

/// <summary>
/// View-model for the recent notifications flyout, listing recently discovered/enriched
/// projects the user has not yet acknowledged (badge count next to "التنبيهات" in the sidebar).
/// Sourced from <see cref="INotificationDispatcher.RecentHistory"/> (bounded, in-memory, no
/// DB persistence per V1 scope).
/// </summary>
public sealed partial class NotificationCenterViewModel : ObservableObject, IDisposable
{
    private readonly INotificationDispatcher _notificationDispatcher;
    private readonly GlobalAppStatusService _globalStatus;
    private bool _isInboxOpen;

    public ObservableCollection<NotificationItemViewModel> RecentNotifications { get; } = [];

    /// <summary>
    /// Unread rows in the ACTIVE source's inbox — the number the rail's bell must show. The
    /// service-level <c>GlobalAppStatusService.UnreadNotificationCount</c> is app-wide, so binding
    /// the bell to it made the badge promise items the source-scoped flyout could never list
    /// (badge &gt; 0 over an empty dropdown). Counting the same rows the flyout renders keeps
    /// badge and list in lockstep.
    /// </summary>
    [ObservableProperty]
    public partial int UnreadBadgeCount { get; set; }

    public GlobalAppStatusService GlobalStatus => _globalStatus;

    public NotificationCenterViewModel(INotificationDispatcher notificationDispatcher, GlobalAppStatusService globalStatus)
    {
        _notificationDispatcher = notificationDispatcher;
        _globalStatus = globalStatus;
        _notificationDispatcher.HistoryChanged += OnHistoryChanged;
        _globalStatus.PropertyChanged += (_, e) =>
        {
            // The flyout is live UI: re-scope rows when the morph changes the projected source.
            if (e.PropertyName is null or nameof(GlobalAppStatusService.ActiveSource))
            {
                MainThread.BeginInvokeOnMainThread(RefreshForCurrentVisibility);
            }
        };
        RefreshBadge();
    }

    /// <summary>
    /// Host hook: while the flyout is closed only the badge is kept current; the row collection is
    /// rebuilt when it is actually opened (the CollectionView is still in the visual tree when
    /// hidden, so repopulating it on every background notification churned native row objects).
    /// </summary>
    public void SetInboxOpen(bool isOpen)
    {
        _isInboxOpen = isOpen;
        if (isOpen)
        {
            RefreshFromHistory();
        }
        else
        {
            RefreshBadge();
        }
    }

    private void RefreshForCurrentVisibility()
    {
        if (_isInboxOpen)
        {
            RefreshFromHistory();
        }
        else
        {
            RefreshBadge();
        }
    }

    private void OnHistoryChanged()
    {
        // HistoryChanged is raised off the grouper's flush path (a background timer callback
        // or a poll-cycle continuation), never guaranteed to be the UI thread.
        // NOTE: the unread badge itself is now incremented once, from the singleton
        // `NotificationDispatcher.HandleFlush` - not here. This view-model is `AddTransient`, so a
        // new instance (and a new subscription to `HistoryChanged`) is created every time the
        // flyout/window is recreated; incrementing a shared counter from here meant every leaked
        // instance counted the same flush again, inflating the badge far past +1 per new project.
        MainThread.BeginInvokeOnMainThread(RefreshForCurrentVisibility);
    }

    /// <summary>Badge only — no row collection churn while the flyout is closed.</summary>
    private void RefreshBadge()
    {
        var active = _globalStatus.ActiveSource;
        UnreadBadgeCount = _notificationDispatcher.RecentHistory
            .Count(project => project.Source == active && project.IsUnread);
    }

    private void RefreshFromHistory()
    {
        // v3 §6: the inbox is scoped to the ACTIVE source (the same live projection the rail
        // morph drives) — a mostaql inbox never mixes in nafezly discoveries. History itself
        // keeps every source's rows, so switching back shows them again untouched.
        var active = _globalStatus.ActiveSource;
        RecentNotifications.Clear();
        foreach (var project in _notificationDispatcher.RecentHistory)
        {
            if (project.Source == active)
            {
                RecentNotifications.Add(new NotificationItemViewModel(project));
            }
        }

        UnreadBadgeCount = RecentNotifications.Count(item => item.IsUnread);
    }

    public void MarkAllAsSeen()
    {
        _globalStatus.ResetUnreadNotificationCount();

        // Flip every currently-listed item's IsUnread flag too (not just the badge counter), so
        // the flyout itself can render read vs. unread rows differently. ProjectSummary isn't
        // INotifyPropertyChanged, so re-populating the ObservableCollection is what makes the
        // CollectionView re-evaluate each row's IsUnread-driven style.
        _notificationDispatcher.MarkHistoryAsRead();
        RefreshFromHistory();
    }

    /// <summary>Unsubscribes from the singleton dispatcher so this transient instance can be collected
    /// without leaving a dangling <see cref="INotificationDispatcher.HistoryChanged"/> handler behind.</summary>
    public void Dispose()
    {
        _notificationDispatcher.HistoryChanged -= OnHistoryChanged;
    }

    [RelayCommand]
    public async Task OpenProjectAsync(NotificationItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        await AppRoutes.NavigateAsync(AppRoutes.ProjectDetails(item.Source, item.ProjectId));
    }
}
