using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MostaqlK.Infrastructure.Database;
using MostaqlK.Models;

namespace MostaqlK.Features.Projects.ViewModels;

/// <summary>
/// View-model for the main project feed page (projects.html): holds the sidebar
/// navigation state and the scrollable collection of <see cref="ProjectCardViewModel"/>.
/// </summary>
public sealed partial class ProjectFeedViewModel : ObservableObject
{
    private readonly IProjectRepository _projectRepository;

    public ObservableCollection<ProjectCardViewModel> Projects { get; } = [];

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private int _unreadCount;

    public ProjectFeedViewModel(IProjectRepository projectRepository)
    {
        _projectRepository = projectRepository;
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            // TODO: load recent projects via `_projectRepository.GetRecentAsync` and populate `Projects`.
            await Task.CompletedTask;
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public void OpenProjectDetails(ProjectSummary project)
    {
        // TODO: navigate to the project details page for `project`.
    }
}
