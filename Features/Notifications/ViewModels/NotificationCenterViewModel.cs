using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
public sealed partial class NotificationCenterViewModel : ObservableObject
{
    private readonly INotificationDispatcher _notificationDispatcher;

    public ObservableCollection<ProjectSummary> RecentNotifications { get; } = [];

    [ObservableProperty]
    public partial int UnreadBadgeCount { get; set; }

    public NotificationCenterViewModel(INotificationDispatcher notificationDispatcher)
    {
        _notificationDispatcher = notificationDispatcher;
        _notificationDispatcher.HistoryChanged += OnHistoryChanged;
        RefreshFromHistory();
    }

    private void OnHistoryChanged()
    {
        // HistoryChanged is raised off the grouper's flush path (a background timer callback
        // or a poll-cycle continuation), never guaranteed to be the UI thread.
        MainThread.BeginInvokeOnMainThread(() =>
        {
            RefreshFromHistory();
            UnreadBadgeCount++;
        });
    }

    private void RefreshFromHistory()
    {
        RecentNotifications.Clear();
        foreach (var project in _notificationDispatcher.RecentHistory)
        {
            RecentNotifications.Add(project);
        }
    }

    public void MarkAllAsSeen()
    {
        UnreadBadgeCount = 0;
    }

    [RelayCommand]
    public async Task OpenProjectAsync(ProjectSummary? project)
    {
        if (project is null)
        {
            return;
        }

        await Shell.Current.GoToAsync($"{nameof(ProjectDetailsPage)}?projectId={project.ProjectId}");
    }
}
