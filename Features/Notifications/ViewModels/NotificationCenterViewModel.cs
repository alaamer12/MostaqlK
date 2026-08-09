using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using MostaqlK.Models;

namespace MostaqlK.Features.Notifications.ViewModels;

/// <summary>
/// View-model for the recent notifications flyout, listing recently discovered/enriched
/// projects the user has not yet acknowledged (badge count next to "التنبيهات" in the sidebar).
/// </summary>
public sealed partial class NotificationCenterViewModel : ObservableObject
{
    public ObservableCollection<ProjectSummary> RecentNotifications { get; } = [];

    [ObservableProperty]
    private int _unreadBadgeCount;

    public void MarkAllAsSeen()
    {
        // TODO: persist "seen" state and reset `UnreadBadgeCount` to 0.
        UnreadBadgeCount = 0;
    }
}
