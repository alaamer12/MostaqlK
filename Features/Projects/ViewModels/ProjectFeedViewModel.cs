using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MostaqlK.Infrastructure.Database;
using MostaqlK.Infrastructure.Database.SearchIndex;
using MostaqlK.Models;

namespace MostaqlK.Features.Projects.ViewModels;

/// <summary>
/// View-model for the main project feed page (projects.html): holds the sidebar
/// navigation state and the scrollable collection of <see cref="ProjectCardViewModel"/>.
/// Drives all four feed states (loading/empty/error/success) and re-queries
/// <see cref="FtsQueryService"/> (or falls back to the reverse-chronological recent list) as the
/// debounced <see cref="SearchQuery"/> changes.
/// </summary>
public sealed partial class ProjectFeedViewModel : ObservableObject
{
    private const int RecentLimit = 100;

    private readonly IProjectRepository _projectRepository;
    private readonly FtsQueryService _ftsQueryService;

    public ObservableCollection<ProjectCardViewModel> Projects { get; } = [];

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isEmpty;

    [ObservableProperty]
    private bool _hasError;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private int _unreadCount;

    /// <summary>True once loading has finished with no error and at least one result — drives the success-state ScrollView.</summary>
    public bool ShowFeed => !IsLoading && !HasError && !IsEmpty;

    public ProjectFeedViewModel(IProjectRepository projectRepository, FtsQueryService ftsQueryService)
    {
        _projectRepository = projectRepository;
        _ftsQueryService = ftsQueryService;
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        IsLoading = true;
        HasError = false;
        ErrorMessage = null;
        try
        {
            var result = string.IsNullOrWhiteSpace(SearchQuery)
                ? await _projectRepository.GetRecentAsync(RecentLimit)
                : await _ftsQueryService.SearchAsync(SearchQuery.Trim());

            if (!result.IsOk)
            {
                HasError = true;
                ErrorMessage = result.Error.ExternalMessage;
                IsEmpty = false;
                return;
            }

            Projects.Clear();
            foreach (var project in result.Value)
            {
                Projects.Add(new ProjectCardViewModel(project, card => _ = SelectProjectAsync(card)));
            }

            UnreadCount = Projects.Count(p => p.IsUnread);
            IsEmpty = Projects.Count == 0;
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(ShowFeed));
        }
    }

    [RelayCommand]
    public async Task RefreshAsync() => await LoadAsync();

    /// <summary>Invoked by <c>SearchInputField.DebouncedCommand</c> once the debounce window elapses.</summary>
    [RelayCommand]
    public async Task SearchAsync(string? query)
    {
        SearchQuery = query ?? string.Empty;
        await LoadAsync();
    }

    [RelayCommand]
    public async Task ClearSearchAsync()
    {
        SearchQuery = string.Empty;
        await LoadAsync();
    }

    [RelayCommand]
    public async Task SelectProjectAsync(ProjectCardViewModel? card)
    {
        if (card is null)
        {
            return;
        }

        card.MarkAsRead();
        UnreadCount = Projects.Count(p => p.IsUnread);

        await Shell.Current.GoToAsync($"ProjectDetailsPage?projectId={card.Project.ProjectId}");
    }
}
