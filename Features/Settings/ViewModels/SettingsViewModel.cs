using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MostaqlK.Infrastructure.Database;
using MostaqlK.Services;
using MostaqlK.Services.Diagnostics;
using MostaqlK.Services.Pipeline;
using Microsoft.Maui.Storage;
using Microsoft.Maui.ApplicationModel;

namespace MostaqlK.Features.Settings.ViewModels;

/// <summary>
/// View-model for the settings panel (settings.html): poll interval/rate, notification
/// grouping mode/threshold, dark mode, and the "مشاريع مضافة اليوم" stat card.
/// Values are persisted via <see cref="Preferences"/> and, on every valid change, pushed
/// live into the running <see cref="IPollService"/>/<see cref="TokenBucketRateLimiter"/>/
/// <see cref="NotificationGrouper"/> instances so changes apply without an app restart.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private const string KeyPollIntervalSeconds = "settings_poll_interval_seconds";
    private const string KeyMaxRequestsPerMinute = "settings_max_requests_per_minute";
    private const string KeyGroupingMode = "settings_grouping_mode";
    private const string KeyGroupingThreshold = "settings_grouping_threshold";
    private const string KeyIsDarkMode = "settings_is_dark_mode";
    private const string KeySafeRequests = "settings_safe_requests";

    // configuration-reference.md: `poll_interval_seconds` default 30, `max_requests_per_minute`
    // default 2. This screen used to advertise a 60s default while PollService itself defaulted to
    // 30, so the two disagreed on an untouched install.
    private const int DefaultPollIntervalSeconds = 30;
    private const int DefaultMaxRequestsPerMinute = TokenBucketRateLimiter.DefaultRequestsPerMinute;
    private const int MinPollIntervalSeconds = 10;
    private const int MaxPollIntervalSeconds = 3600;

    private readonly IPollService _pollService;
    private readonly TokenBucketRateLimiter _rateLimiter;
    private readonly NotificationGrouper _grouper;
    private readonly IProjectRepository _projectRepository;
    private readonly GlobalAppStatusService _globalStatus;
    private bool _isLoading;

    [ObservableProperty]
    public partial int PollIntervalSeconds { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SafeRequestsHintText))]
    public partial int RequestsPerMinute { get; set; }

    /// <summary>
    /// "الطلبات الآمنة" - when on (the default), <see cref="RequestsPerMinute"/> is enforced exactly
    /// as <c>worker-pool-and-rate-limiter.md</c> describes: the bucket holds at most that many
    /// tokens, refills at <c>rpm / 60</c> per second and spaces consecutive requests by one second.
    /// When off, the limiter allows the same <see cref="RequestsPerMinute"/> burst but refills
    /// <see cref="TokenBucketRateLimiter.FastModeRefillMultiplier"/> times faster with no spacing,
    /// which drains a large backlog much faster but sends far more traffic to mostaql.com in a
    /// short window.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SafeRequestsHintText))]
    public partial bool SafeRequests { get; set; } = true;

    /// <summary>Tooltip body for the "الطلبات الآمنة" row's (i) affordance.</summary>
    public string SafeRequestsHintText => SafeRequests
        ? $"مُفعّل: يتم توزيع الطلبات على مدار الدقيقة بحد {RequestsPerMinute} طلب/دقيقة مع فاصل ثانية واحدة بين كل طلبين، وهو السلوك الموصى به لتجنّب الحجب من الموقع."
        : $"مُعطّل: يُسمح بإرسال حتى {RequestsPerMinute} طلبات دفعة واحدة ثم تعبئتها من جديد بمعدل أسرع ({TokenBucketRateLimiter.FastModeRefillMultiplier:0}×)، فتُعالج القائمة أسرع بكثير لكن مع خطر أعلى للحجب.";

    [ObservableProperty]
    public partial NotificationGroupingMode GroupingMode { get; set; }

    [ObservableProperty]
    public partial int GroupingThreshold { get; set; }

    [ObservableProperty]
    public partial bool IsDarkMode { get; set; }

    public GlobalAppStatusService GlobalStatus => _globalStatus;

    /// <summary>
    /// Validation feedback surfaced to the UI (reusing <c>LabelWithSubText</c>'s
    /// external/fix message shape). Empty when every field is currently valid.
    /// </summary>
    [ObservableProperty]
    public partial string ValidationMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasValidationError { get; set; }

    public SettingsViewModel(
        IPollService pollService,
        TokenBucketRateLimiter rateLimiter,
        NotificationGrouper grouper,
        IProjectRepository projectRepository,
        GlobalAppStatusService globalStatus)
    {
        _pollService = pollService;
        _rateLimiter = rateLimiter;
        _grouper = grouper;
        _projectRepository = projectRepository;
        _globalStatus = globalStatus;

        LoadFromPreferences();
        _ = LoadProjectsAddedTodayAsync();
    }

    private void LoadFromPreferences()
    {
        _isLoading = true;

        PollIntervalSeconds = Preferences.Get(KeyPollIntervalSeconds, DefaultPollIntervalSeconds);
        RequestsPerMinute = Preferences.Get(KeyMaxRequestsPerMinute, DefaultMaxRequestsPerMinute);
        SafeRequests = Preferences.Get(KeySafeRequests, true);
        GroupingThreshold = Preferences.Get(KeyGroupingThreshold, 5);
        // Seed from the theme the app already resolved at startup (App.xaml.cs, which honours a
        // `--theme=light|dark` argument over the stored preference) and only fall back to the
        // preference when no explicit theme was applied. Reading the preference unconditionally
        // here would silently undo that startup override the moment this page is constructed.
        IsDarkMode = Application.Current?.UserAppTheme switch
        {
            AppTheme.Dark => true,
            AppTheme.Light => false,
            _ => Preferences.Get(KeyIsDarkMode, false),
        };

        var storedMode = Preferences.Get(KeyGroupingMode, nameof(NotificationGroupingMode.EndOfMinute));
        GroupingMode = Enum.TryParse<NotificationGroupingMode>(storedMode, out var parsedMode)
            ? parsedMode
            : NotificationGroupingMode.EndOfMinute;

        _isLoading = false;

        // Apply the loaded values to the live pipeline services right away, so the running
        // instances always reflect whatever was last persisted (e.g. after an app restart).
        ApplyPollSettings();
        ApplyGroupingSettings();
    }

    private async Task LoadProjectsAddedTodayAsync()
    {
        var result = await _projectRepository.CountAddedTodayAsync();
        if (!result.IsError)
        {
            _globalStatus.SetProjectsAddedToday(result.Value);
        }
    }

    partial void OnPollIntervalSecondsChanged(int value)
    {
        if (_isLoading)
        {
            return;
        }

        if (value < MinPollIntervalSeconds || value > MaxPollIntervalSeconds)
        {
            SetValidationError($"فترة الفحص يجب أن تكون بين {MinPollIntervalSeconds} و {MaxPollIntervalSeconds} ثانية.");
            return;
        }

        ClearValidationError();
        Preferences.Set(KeyPollIntervalSeconds, value);
        ApplyPollSettings();
    }

    partial void OnRequestsPerMinuteChanged(int value)
    {
        if (_isLoading)
        {
            return;
        }

        if (value <= 0)
        {
            SetValidationError("الحد الأقصى للطلبات يجب أن يكون رقمًا أكبر من صفر.");
            return;
        }

        ClearValidationError();
        Preferences.Set(KeyMaxRequestsPerMinute, value);
        ApplyPollSettings();
    }

    partial void OnSafeRequestsChanged(bool value)
    {
        if (_isLoading)
        {
            return;
        }

        Preferences.Set(KeySafeRequests, value);
        ApplyPollSettings();
    }

    partial void OnGroupingModeChanged(NotificationGroupingMode value)
    {
        if (_isLoading)
        {
            return;
        }

        Preferences.Set(KeyGroupingMode, value.ToString());
        ApplyGroupingSettings();
    }

    partial void OnGroupingThresholdChanged(int value)
    {
        if (_isLoading)
        {
            return;
        }

        if (value <= 0 && GroupingMode != NotificationGroupingMode.EndOfMinute)
        {
            SetValidationError("حد التجميع يجب أن يكون رقمًا أكبر من صفر.");
            return;
        }

        ClearValidationError();
        Preferences.Set(KeyGroupingThreshold, value);
        ApplyGroupingSettings();
    }

    partial void OnIsDarkModeChanged(bool value)
    {
        if (_isLoading)
        {
            return;
        }

        Preferences.Set(KeyIsDarkMode, value);
        ApplyTheme();
    }

    private void ApplyPollSettings()
    {
        _pollService.PollIntervalSeconds = PollIntervalSeconds;

        // The limiter derives capacity, refill and spacing from these two values itself, so the
        // numbers can no longer drift apart from the configured budget.
        _rateLimiter.Reconfigure(RequestsPerMinute, SafeRequests);
        OnPropertyChanged(nameof(SafeRequestsHintText));
    }

    private void ApplyGroupingSettings()
    {
        _grouper.Mode = GroupingMode;
        _grouper.Enabled = true;

        if (GroupingMode == NotificationGroupingMode.AfterMinutes)
        {
            _grouper.AfterMinutesThreshold = Math.Max(1, GroupingThreshold);
        }
        else if (GroupingMode == NotificationGroupingMode.AfterCount)
        {
            _grouper.AfterCountThreshold = Math.Max(1, GroupingThreshold);
        }
    }

    private void ApplyTheme()
    {
        // Dark-mode gets applied via MAUI's built-in per-app theme switch. The sidebar's own
        // dark-mode row (AppSidebar.OnDarkModeToggleToggled) applies the same UserAppTheme/
        // Preferences write directly so it works from every page, not just Settings; both stay
        // in sync via Application.RequestedThemeChanged.
        if (Application.Current is { } app)
        {
            app.UserAppTheme = IsDarkMode ? AppTheme.Dark : AppTheme.Light;
        }
    }

    private void SetValidationError(string message)
    {
        ValidationMessage = message;
        HasValidationError = true;
    }

    private void ClearValidationError()
    {
        ValidationMessage = string.Empty;
        HasValidationError = false;
    }

    [TraceInteraction("SaveCommand")]
    [MostaqlK.Core.ErrorOutcome(MostaqlK.Core.ErrorOutcome.Rethrown, Label = "SaveCommand")]
    [RelayCommand]
    public Task SaveAsync()
    {
        using var _ = TraceScope.Begin("SaveCommand");
        try
        {
            // Every field already persists+applies live on change (see the OnXChanged partials
            // above), so Save is a convenience no-op that simply confirms nothing is pending.
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _.MarkFaulted(ex);
            throw;
        }
    }
}
