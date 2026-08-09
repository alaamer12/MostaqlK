using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MostaqlK.Models;

namespace MostaqlK.Features.Projects.ViewModels;

/// <summary>
/// View-model backing a single <c>ProjectCard</c> in the project feed, wrapping a
/// <see cref="ProjectSummary"/> with the observable unread/read state shown in projects.html.
/// </summary>
public sealed partial class ProjectCardViewModel : ObservableObject
{
    private readonly Action<ProjectCardViewModel>? _onSelected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsUnread))]
    private ProjectSummary _project;

    /// <summary>Bound by <c>ProjectCard.xaml</c>'s tap gesture; delegates back to the owning feed's select handler.</summary>
    public ICommand SelectCommand { get; }

    public ProjectCardViewModel(ProjectSummary project, Action<ProjectCardViewModel>? onSelected = null)
    {
        _project = project;
        _onSelected = onSelected;
        SelectCommand = new RelayCommand(() => _onSelected?.Invoke(this));
    }

    public bool IsUnread => Project.IsUnread;

    public string Title => Project.Title;

    public string ClientName => Project.ClientName;

    public string PostedRelative => Project.PostedRelative;

    public int ProposalCount => Project.ProposalCount;

    public void MarkAsRead()
    {
        // TODO: persist the read state via IProjectRepository once an UpdateReadStateAsync
        // method is added to the repository — the AppCard/UI reflects it immediately either way.
        if (!Project.IsUnread)
        {
            return;
        }

        Project.IsUnread = false;
        OnPropertyChanged(nameof(IsUnread));
    }
}
