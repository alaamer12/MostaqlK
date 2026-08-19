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

    public ObservableCollection<NotificationItemViewModel> RecentNotifications { get; } = [];

    public GlobalAppStatusService GlobalStatus => _globalStatus;

    public NotificationCenterViewModel(INotificationDispatcher notificationDispatcher, GlobalAppStatusService globalStatus)
    {
        _notificationDispatcher = notificationDispatcher;
        _globalStatus = globalStatus;
        _notificationDispatcher.HistoryChanged += OnHistoryChanged;
        RefreshFromHistory();
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
        MainThread.BeginInvokeOnMainThread(RefreshFromHistory);
    }

    private void RefreshFromHistory()
    {
        RecentNotifications.Clear();
        foreach (var project in _notificationDispatcher.RecentHistory)
        {
            RecentNotifications.Add(new NotificationItemViewModel(project));
        }
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

        await AppRoutes.NavigateAsync(AppRoutes.ProjectDetails(item.ProjectId));
    }
}
