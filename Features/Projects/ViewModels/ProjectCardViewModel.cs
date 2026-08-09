using CommunityToolkit.Mvvm.ComponentModel;
using MostaqlK.Models;

namespace MostaqlK.Features.Projects.ViewModels;

/// <summary>
/// View-model backing a single <c>ProjectCard</c> in the project feed, wrapping a
/// <see cref="ProjectSummary"/> with the observable unread/read state shown in projects.html.
/// </summary>
public sealed partial class ProjectCardViewModel : ObservableObject
{
    [ObservableProperty]
    private ProjectSummary _project;

    public ProjectCardViewModel(ProjectSummary project)
    {
        _project = project;
    }

    public bool IsUnread => Project.IsUnread;

    public string Title => Project.Title;

    public string ClientName => Project.ClientName;

    public string PostedRelative => Project.PostedRelative;

    public int ProposalCount => Project.ProposalCount;

    public void MarkAsRead()
    {
        // TODO: persist the read state via IProjectRepository once implemented.
        Project.IsUnread = false;
        OnPropertyChanged(nameof(IsUnread));
    }
}
