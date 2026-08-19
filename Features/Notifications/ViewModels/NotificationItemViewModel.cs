using CommunityToolkit.Mvvm.ComponentModel;
using MostaqlK.Core.Formatting;
using MostaqlK.Models;

namespace MostaqlK.Features.Notifications.ViewModels;

/// <summary>
/// View-model wrapping a single <see cref="ProjectSummary"/> for the notification center flyout,
/// exposing dynamic relative time computation and bindable unread state.
/// </summary>
public sealed partial class NotificationItemViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProjectId))]
    [NotifyPropertyChangedFor(nameof(Title))]
    [NotifyPropertyChangedFor(nameof(IsUnread))]
    [NotifyPropertyChangedFor(nameof(PostedRelative))]
    [NotifyPropertyChangedFor(nameof(Url))]
    public partial ProjectSummary Project { get; set; }

    public NotificationItemViewModel(ProjectSummary project)
    {
        Project = project;
    }

    public long ProjectId => Project.ProjectId;

    public string Title => Project.Title;

    public bool IsUnread => Project.IsUnread;

    /// <summary>Dynamic relative time calculated from the project's discovered timestamp.</summary>
    public string PostedRelative => ArabicRelativeTime.Since(Project.DiscoveredAt);

    public string? Url => Project.Url;
}
