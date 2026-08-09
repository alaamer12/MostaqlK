using CommunityToolkit.Mvvm.ComponentModel;

namespace MostaqlK.Features.Projects.ViewModels;

/// <summary>
/// View-model for the status bar area shown alongside the project feed: last poll time,
/// pipeline activity indicator, and unread notification count.
/// </summary>
public sealed partial class StatusBarViewModel : ObservableObject
{
    [ObservableProperty]
    private DateTimeOffset? _lastPolledAt;

    [ObservableProperty]
    private bool _isPolling;

    [ObservableProperty]
    private int _unreadCount;
}
