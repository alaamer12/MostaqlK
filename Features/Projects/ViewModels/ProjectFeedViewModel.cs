using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.Storage;
using MostaqlK.Infrastructure.Database;
using MostaqlK.Infrastructure.Database.SearchIndex;
using MostaqlK.Models;
using MostaqlK.Services.Diagnostics;
using MostaqlK.Services.Pipeline;

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
    private const string KeyPollIntervalSeconds = "settings_poll_interval_seconds";
    private const string KeyMaxRequestsPerMinute = "settings_max_requests_per_minute";

    private readonly IProjectRepository _projectRepository;
    private readonly FtsQueryService _ftsQueryService;
    private readonly IPollService _pollService;
    private readonly TokenBucketRateLimiter _rateLimiter;

    public ObservableCollection<ProjectCardViewModel> Projects { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSearchActive))]
    [NotifyPropertyChangedFor(nameof(EmptyStateTitle))]
    [NotifyPropertyChangedFor(nameof(EmptyStateSubtitle))]
    public partial string SearchQuery { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial bool IsEmpty { get; set; }

    [ObservableProperty]
    public partial bool HasError { get; set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UnreadCountText))]
    public partial int UnreadCount { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TrackedCountText))]
    public partial int TrackedCount { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProjectsAddedTodayText))]
    public partial int ProjectsAddedTodayCount { get; set; }

    public string ProjectsAddedTodayText => ProjectsAddedTodayCount.ToString();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PollIntervalText))]
    [NotifyPropertyChangedFor(nameof(LiveStatusText))]
    [NotifyPropertyChangedFor(nameof(PollToggleLabel))]
    public partial bool IsPollingActive { get; set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PollIntervalText))]
    public partial int PollIntervalSeconds { get; set; } = 30;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RateLimitText))]
    public partial int RequestsPerMinute { get; set; } = 12;

    [ObservableProperty]
    public partial string LastScanText { get; set; } = "آخر فحص: منذ لحظات";

    /// <summary>True once loading has finished with no error and at least one result — drives the success-state ScrollView.</summary>
    public bool ShowFeed => !IsLoading && !HasError && !IsEmpty;

    public string PollIntervalText => IsPollingActive
        ? $"يتم الفحص كل {PollIntervalSeconds} ثانية"
        : "الفحص متوقف";

    public string RateLimitText => $"{RequestsPerMinute} طلب / دقيقة";

    public string LiveStatusText => IsPollingActive ? "مباشر" : "متوقف";

    public string PollToggleLabel => IsPollingActive ? "إيقاف" : "بدء الفحص";

    public string TrackedCountText => IsSearchActive
        ? $"{TrackedCount} نتيجة مطابقة"
        : $"{TrackedCount} مشروع متتبَّع";

    public string UnreadCountText => $"{UnreadCount} غير مقروء";

    /// <summary>True while a search term is active — drives filtered footer counts and the empty-state copy.</summary>
    public bool IsSearchActive => !string.IsNullOrWhiteSpace(SearchQuery);

    public string EmptyStateTitle => IsSearchActive
        ? $"لا توجد نتائج لـ \"{SearchQuery.Trim()}\""
        : "لا توجد مشاريع حالياً";

    public string EmptyStateSubtitle => IsSearchActive
        ? "جرّب كلمات بحث مختلفة أو تحقّق من الإملاء."
        : "سيتم عرض المشاريع الجديدة هنا فور توفرها.";

    public ProjectFeedViewModel(
        IProjectRepository projectRepository,
        FtsQueryService ftsQueryService,
        IPollService pollService,
        TokenBucketRateLimiter rateLimiter)
    {
        _projectRepository = projectRepository;
        _ftsQueryService = ftsQueryService;
        _pollService = pollService;
        _rateLimiter = rateLimiter;

        RefreshHeaderStatus();
    }

    public void RefreshHeaderStatus()
    {
        PollIntervalSeconds = Preferences.Get(KeyPollIntervalSeconds, _pollService.PollIntervalSeconds);
        RequestsPerMinute = Preferences.Get(KeyMaxRequestsPerMinute, Math.Max(1, _rateLimiter.Capacity));
        IsPollingActive = !_pollService.IsPaused;
        LastScanText = "آخر فحص: منذ لحظات";
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        IsLoading = true;
        HasError = false;
        ErrorMessage = null;
        RefreshHeaderStatus();
        try
        {
            var today = await _projectRepository.CountAddedTodayAsync();
            if (today.IsOk)
            {
                ProjectsAddedTodayCount = today.Value;
            }

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

            if (IsSearchActive)
            {
                // While a search filter is active, the footer should reflect the filtered
                // (visible) results rather than the whole store's totals.
                TrackedCount = Projects.Count;
                UnreadCount = Projects.Count(p => p.IsUnread);
            }
            else
            {
                // No filter active: status-bar totals count the whole store, not just the
                // page of rows loaded above.
                var tracked = await _projectRepository.CountTrackedAsync();
                if (tracked.IsOk)
                {
                    TrackedCount = tracked.Value.Tracked;
                    UnreadCount = tracked.Value.Unread;
                }
                else
                {
                    TrackedCount = Projects.Count;
                    UnreadCount = Projects.Count(p => p.IsUnread);
                }
            }

            IsEmpty = Projects.Count == 0;
            LastScanText = "آخر فحص: منذ لحظات";
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(ShowFeed));
        }
    }

    [TraceInteraction("RefreshCommand")]
    [MostaqlK.Core.ErrorOutcome(MostaqlK.Core.ErrorOutcome.Rethrown, Label = "RefreshCommand")]
    [RelayCommand]
    public async Task RefreshAsync()
    {
        using var _ = TraceScope.Begin("RefreshCommand");
        try
        {
            await LoadAsync();
        }
        catch (Exception ex)
        {
            _.MarkFaulted(ex);
            throw;
        }
    }

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

    [TraceInteraction("TogglePolling")]
    [MostaqlK.Core.ErrorOutcome(MostaqlK.Core.ErrorOutcome.Rethrown, Label = "TogglePolling")]
    [RelayCommand]
    public void TogglePolling()
    {
        using var _ = TraceScope.Begin("TogglePolling", new { IsPollingActive });
        try
        {
            var nextPaused = IsPollingActive;
            _pollService.SetPaused(nextPaused);
            IsPollingActive = !_pollService.IsPaused;
            OnPropertyChanged(nameof(PollIntervalText));
            OnPropertyChanged(nameof(LiveStatusText));
            OnPropertyChanged(nameof(PollToggleLabel));
        }
        catch (Exception ex)
        {
            _.MarkFaulted(ex);
            throw;
        }
    }

    [RelayCommand]
    public void MarkAllRead()
    {
        foreach (var project in Projects)
        {
            project.MarkAsRead();
        }

        UnreadCount = Projects.Count(p => p.IsUnread);
    }

    [TraceInteraction("SelectCommand")]
    [MostaqlK.Core.ErrorOutcome(MostaqlK.Core.ErrorOutcome.Rethrown, Label = "SelectCommand")]
    [RelayCommand]
    public async Task SelectProjectAsync(ProjectCardViewModel? card)
    {
        using var _ = TraceScope.Begin("SelectCommand", new { ProjectId = card?.Project.ProjectId });
        try
        {
            if (card is null)
            {
                return;
            }

            card.MarkAsRead();
            UnreadCount = Projects.Count(p => p.IsUnread);

            await Shell.Current.GoToAsync($"ProjectDetailsPage?projectId={card.Project.ProjectId}");
        }
        catch (Exception ex)
        {
            _.MarkFaulted(ex);
            throw;
        }
    }
}
