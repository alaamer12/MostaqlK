using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Storage;
using MostaqlK.Core.Platform;
using MostaqlK.Core.Sources;
using MostaqlK.Infrastructure.Database;
using MostaqlK.Services;
using MostaqlK.Services.Diagnostics;
using MostaqlK.Services.Pipeline;
using MostaqlK.Services.Pipeline.WorkerPool;
using MostaqlK.UI.DesignSystem;
using MostaqlK.UI.PlatformComponents.SettingsRow;

namespace MostaqlK.Features.Settings.ViewModels;

/// <summary>
/// View-model for the settings panel (settings.html): poll interval/rate, notification
/// grouping mode/threshold, dark mode, and the "مشاريع مضافة اليوم" stat card.
/// Values are persisted through <see cref="ISettingsRepository"/> (per-source for the pipeline
/// knobs, global for dark mode/startup) and, on every valid change, pushed live into the running
/// <see cref="IPollService"/>/<see cref="TokenBucketRateLimiter"/>/<see cref="NotificationGrouper"/>
/// instances so changes apply without an app restart. The persistence WRITE is deferred through
/// <see cref="MostaqlK.Core.Platform.DeferredPreferences"/> (ordered last-wins queue): a keystroke
/// never blocks on the settings-file round-trip; the flush lands on Save or app teardown.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    // The per-source values are stored in this source's namespaced settings container, read back
    // through the repository's legacy flat-key fallback (see ISettingsRepository). Dark mode,
    // startup, and polling-active stay deliberately global (app behavior, not per-source).
    /// <summary>
    /// The source whose namespaced preferences this transient VM edits — the live ACTIVE source
    /// (v3 morph), not a startup snapshot: the page is reconstructed per opening, so it always
    /// edits what the rail currently projects (contract §4).
    /// </summary>
    private SourceId _source => _globalStatus?.ActiveSource ?? SourceId.Default;

    // No bounds, no defaults, no Arabic messages here any more: every one of them is declared on
    // the setting's descriptor in `SettingsCatalog.Builtin` and read back through the composed
    // catalog (see `DefaultOf` / `SettingsValidator`). This screen used to keep its own copies of
    // all four, which is how it came to advertise a 60s cadence while `PollService` defaulted to
    // 30, and a parallel-fetch ceiling of 16 that `WorkerPool` never enforced.

    /// <summary>Floor for how long the cookie drop zone's spinner stays visible, in milliseconds.</summary>
    private const int SpinnerMinimumVisibleMilliseconds = 500;

    /// <summary>
    /// The catalog key of the session-cookie composite. A row identity in the composed list, not a
    /// persisted preference (the cookie itself lives in <c>CookieStore</c>), so it is not a
    /// <c>SettingsKeys</c> member.
    /// </summary>
    private const string SessionCookieRowKey = "session-cookie";

    private readonly IPollService _pollService;
    private readonly TokenBucketRateLimiter _rateLimiter;
    private readonly WorkerPool _workerPool;
    private readonly NotificationGrouper _grouper;
    private readonly IEnrichmentService _enrichmentService;
    private readonly IProjectRepository _projectRepository;
    private readonly GlobalAppStatusService _globalStatus;
    private readonly ICookieStoreProvider _cookieStores;
    private readonly CloseBehaviorService _closeBehaviorService;
    private readonly IStartupService _startupService;
    private readonly ISettingsRepository _settings;
    private readonly SourceContributionRegistry _contributions;
    private readonly IServiceProvider _services;
    private bool _isLoading;

    [ObservableProperty]
    public partial bool StartupEnabled { get; set; }

    /// <summary>Whether the Launch on Startup setting is supported and visible on this platform.</summary>
    public bool IsStartupVisible => _startupService.IsSupported;

    [ObservableProperty]
    public partial int PollIntervalSeconds { get; set; }

    [ObservableProperty]
        public partial int RequestsPerMinute { get; set; }

    [ObservableProperty]
    public partial int MaxConcurrentDetailFetches { get; set; }

    [ObservableProperty]
    public partial string QueryParams { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IncludeAssets { get; set; }

    [ObservableProperty]
    public partial bool NotificationGroupingEnabled { get; set; }

    /// <summary>
    /// "الطلبات الآمنة" - when on (the default), <see cref="RequestsPerMinute"/> is enforced exactly
    /// as <c>worker-pool-and-rate-limiter.md</c> describes: the bucket holds at most that many
    /// tokens, refills at <c>rpm / 60</c> per second and spaces consecutive requests by one second.
    /// When off, the limiter allows the same <see cref="RequestsPerMinute"/> burst but refills
    /// <see cref="TokenBucketRateLimiter.FastModeRefillMultiplier"/> times faster with no spacing,
    /// which drains a large backlog much faster but sends far more traffic to the monitored site in a
    /// short window.
    /// </summary>
    [ObservableProperty]
        public partial bool SafeRequests { get; set; } = true;

    /// <summary>Tooltip body for the "الطلبات الآمنة" row's (i) affordance.</summary>
    public string SafeRequestsHintText => SafeRequests
        ? $"مُفعّل: يتم توزيع الطلبات على مدار الدقيقة بحد {RequestsPerMinute} طلب/دقيقة مع فاصل ثانية واحدة بين كل طلبين، وهو السلوك الموصى به لتجنّب الحجب من الموقع."
        : $"مُعطّل: يُسمح بإرسال حتى {RequestsPerMinute} طلبات دفعة واحدة ثم تعبئتها من جديد بمعدل أسرع ({TokenBucketRateLimiter.FastModeRefillMultiplier:0}×)، فتُعالج القائمة أسرع بكثير لكن مع خطر أعلى للحجب.";

    /// <summary>Helper copy under the query-params input; the source name comes from its descriptor.</summary>
    public string QueryParamsHelperText =>
        $"معاملات URL إضافية تُمرَّر لصفحة المشاريع في {SourceName} (مثل: category=software-dev)";

    /// <summary>Session-cookie section header; Arabic + Latin source names both come from the descriptor.</summary>
    public string CookieSectionTitle =>
        $"كوكي جلسة {SourceName} ({SourceNameLatin} Session Cookie)";

    /// <summary>
    /// Cosmetic sentence-case of the descriptor's session cookie name for placeholder copy
    /// (the storage/parsing fact stays the exact lowercase value).
    /// </summary>
    private string SessionCookieNameDisplay
    {
        get
        {
            var name = MostaqlK.Core.Sources.SourceRegistryProvider.DescriptorFor(_source)?.SessionCookieName;
            if (string.IsNullOrEmpty(name))
            {
                return "Cookie";
            }

            return char.ToUpperInvariant(name[0]) + name[1..];
        }
    }

    /// <summary>Input hint naming just the session cookie key ("Mostaql_session...").</summary>
    public string SessionKeyPlaceholder => $"{SessionCookieNameDisplay}...";

    /// <summary>Full paste instruction for the cookie field.</summary>
    public string CookiePastePlaceholder => $"الصق قيمة {SessionCookieNameDisplay} هنا...";

    private string SourceName => MostaqlK.Core.Sources.SourceRegistryProvider.DescriptorFor(_source)?.DisplayNameAr ?? "المصدر";
    private string SourceNameLatin => MostaqlK.Core.Sources.SourceRegistryProvider.DescriptorFor(_source)?.DisplayNameLatin ?? SourceName;

    [ObservableProperty]
    public partial NotificationGroupingMode GroupingMode { get; set; }

    [ObservableProperty]
    public partial int GroupingThreshold { get; set; }

    [ObservableProperty]
    public partial bool IsDarkMode { get; set; }

    /// <summary>
    /// "تذكر آخر منصة مفتوحة" — a deliberately-GLOBAL bool (default on). Persisting the flag is the
    /// settings page's whole job here; the startup-restore that honours it (re-projecting the
    /// remembered <see cref="SettingsKeys.ActiveSource"/>) lives outside this VM and is wired by the
    /// parent, so this file must never touch App startup. Read/written through the single
    /// <see cref="ISettingsRepository"/> global path exactly like dark mode / startup.
    /// </summary>
    [ObservableProperty]
    public partial bool RememberLastSource { get; set; } = true;

    /// <summary>
    /// "تنبيهات هذه المنصة" — the per-source notifications on/off switch (v3 mockup). Persisted in
    /// the active source's namespaced container. NOTE: the toast dispatcher does not yet read this
    /// flag; it is exposed cleanly (key + property) so the parent can wire dispatch-side gating.
    /// </summary>
    [ObservableProperty]
    public partial bool SourceNotificationsEnabled { get; set; } = true;

    /// <summary>Number of projects stored for the active source ("الأرشيف" row count).</summary>
    [ObservableProperty]
    public partial int ArchiveCount { get; set; }

    /// <summary>Reveals the inline session-cookie editor behind the "جلسة الإثراء" row's "ربط" link.</summary>
    [ObservableProperty]
    public partial bool IsCookieEditorOpen { get; set; }


    /// <summary>Total stored projects for the active source, for the "الأرشيف" row's "{n} مشروعًا".</summary>
    public string ArchiveCountText => $"{ArchiveCount} مشروعًا";

    partial void OnArchiveCountChanged(int value) => OnPropertyChanged(nameof(ArchiveCountText));

    // -----------------------------------------------------------------
    // Descriptor-driven row visibility (v3 §9: platform-scoped settings).
    // Every flag reads the ACTIVE source's ISourceDescriptor (capabilities / currency), so a
    // source that lacks a capability never renders its row (absent = never rendered, never empty)
    // — zero source-id branches (contract §10 hard rule).
    // -----------------------------------------------------------------

    private MostaqlK.Core.Sources.ISourceDescriptor? ActiveDescriptor =>
        MostaqlK.Core.Sources.SourceRegistryProvider.DescriptorFor(_source);

    /// <summary>Arabic platform name for the header caption "إعدادات المنصة الحالية: …".</summary>
    public string ActiveSourceName => ActiveDescriptor?.DisplayNameAr ?? "المنصة";

    /// <summary>Single-letter rail glyph for the header caption's tile.</summary>
    public string ActiveSourceGlyph => ActiveDescriptor?.RailGlyph ?? "•";

    /// <summary>Baked tile icon for the header caption's tile (descriptor data; null → letter fallback).</summary>
    public Microsoft.Maui.Controls.ImageSource? ActiveSourceGlyphImage =>
        MostaqlK.UI.PlatformComponents.SourceRail.SourceGlyphImage.TryLoad(ActiveDescriptor);

    /// <summary>The letter tile shows only when the source's baked icon is absent.</summary>
    public bool ShowsSourceGlyphLetter => ActiveSourceGlyphImage is null;

    partial void OnHasSessionCookieChanged(bool value)
    {
                NotifyCookieLinkCommands();
        // The composite's status tone (green when a session exists) is part of its descriptor copy,
        // so the row is rebuilt with it.
        ComposeGroups();
    }

    partial void OnIsCookieEditorOpenChanged(bool value)
    {
        if (_rowsByKey.TryGetValue(SessionCookieRowKey, out var row))
        {
            row.IsExpanded = value;
        }
    }


    public GlobalAppStatusService GlobalStatus => _globalStatus;

    // -----------------------------------------------------------------
    // Catalog-driven surface (settings plugin step 4).
    // `Groups` is what the page will render once the hardcoded layouts are deleted: one row per
    // composed descriptor, built by the `SettingsRow` unit from its descriptor alone. Until step 7
    // the same values are ALSO exposed as the historic per-setting properties (the shipped layouts
    // bind them), so the row callbacks read and write through those properties rather than opening
    // a second persistence path — one mutation route, one live-apply route, no chance for the two
    // surfaces to disagree. The key switch below is that temporary bridge and is deleted with the
    // properties, not grown.
    // -----------------------------------------------------------------

    /// <summary>Sections + rows in catalog order, recomposed on every source switch.</summary>
    public IReadOnlyList<SettingsGroupViewModel> Groups { get; private set; } = [];

    private ComposedSettings _composed = new([], []);
    private readonly Dictionary<string, SettingsRowViewModel> _rowsByKey = new(StringComparer.Ordinal);

    /// <summary>The composed descriptor for a key, or null when this source does not show it.</summary>
    private SettingsDescriptor? DescriptorOf(string key) => _composed.Descriptors.FirstOrDefault(d => d.Key == key);

    /// <summary>
    /// The default a setting falls back to when nothing is stored: the descriptor's literal, or the
    /// fact it points at on the source descriptor (cadence/rate), so the number exists once.
    /// </summary>
    private int IntDefaultOf(string key, int fallback)
    {
        var d = DescriptorOf(key);
        if (d is null)
        {
            return fallback;
        }

        return d.DefaultFrom switch
        {
            DefaultFact.PollIntervalSeconds => ActiveDescriptor?.DefaultPollIntervalSeconds ?? fallback,
            DefaultFact.RequestsPerMinute => ActiveDescriptor?.DefaultRequestsPerMinute ?? fallback,
            _ => int.TryParse(d.DefaultLiteral, out var parsed) ? parsed : fallback,
        };
    }

    private bool BoolDefaultOf(string key, bool fallback)
    {
        var d = DescriptorOf(key);
        return d is null || !bool.TryParse(d.DefaultLiteral, out var parsed) ? fallback : parsed;
    }

    private string StringDefaultOf(string key, string fallback)
    {
        var d = DescriptorOf(key);
        return d?.DefaultLiteral ?? fallback;
    }

    private void ComposeGroups()
    {
        var descriptor = ActiveDescriptor;
        if (descriptor is null)
        {
            _composed = new ComposedSettings([], []);
            Groups = [];
            _rowsByKey.Clear();
            OnPropertyChanged(nameof(Groups));
            return;
        }

        _composed = SettingsCatalog.Compose(descriptor, _contributions.SettingsContribsFor(_source));

        _rowsByKey.Clear();
        Groups = _composed.Descriptors
            .GroupBy(d => d.Section)
            .Select(group =>
            {
                // A section's Arabic caption is data too: the module that invents a section
                // contributes a SectionHeader row for it, and that row renders first in the group —
                // the shared page holds no key→label map and names no source.
                var header = group.FirstOrDefault(d => d.Shape == SettingsShape.SectionHeader);
                var rows = new List<SettingsRowViewModel>();
                foreach (var descriptor in group)
                {
                    // The section band owns the divider under itself, so the row right after it
                    // must not draw a second line.
                    rows.Add(BuildRow(descriptor, isFirstInSection: rows.Count == 0));
                }

                return new SettingsGroupViewModel(header?.LabelAr ?? group.Key, rows);
            })
            .ToList();

        OnPropertyChanged(nameof(Groups));
    }

    private SettingsRowViewModel BuildRow(SettingsDescriptor d, bool isFirstInSection = false)
    {
        // The cookie composite's status line is green only when a session actually exists, and the
        // editor band is open only while the user is pasting — both are live state, so the row gets
        // its own copy of the descriptor rather than the catalog mutating per source.
        if (d.Shape == SettingsShape.CookieEditor)
        {
            d = d with { HintTone = HasSessionCookie ? SettingsHintTone.Success : SettingsHintTone.Muted };
        }

        var row = new SettingsRowViewModel(
            d,
            read: () => RowValueOf(d),
            write: v => RowWrite(d, v),
            command: RowCommandOf(d),
            valueText: RowValueTextOf(d),
            commands: RowCommandsOf(d))
        {
            // Position facts, decided here rather than guessed by the renderer.
            ShowTopDivider = d.Shape != SettingsShape.SectionHeader && !isFirstInSection,
            LastScanAt = d.CommandName == "last-scan" ? _globalStatus.LastScanCompletedAt : null,
        };

        if (d.Shape == SettingsShape.CookieEditor)
        {
            row.IsExpanded = IsCookieEditorOpen;
        }

        _rowsByKey[d.Key] = row;
        return row;
    }

    /// <summary>
    /// The cookie row's four affordances as named commands. Which of the two links is visible is
    /// decided by their <c>CanExecute</c> (a session exists or not), so the renderer asks no question
    /// about cookies at all.
    /// </summary>
    private IReadOnlyDictionary<string, System.Windows.Input.ICommand>? RowCommandsOf(SettingsDescriptor d)
    {
        if (d.Shape != SettingsShape.CookieEditor)
        {
            return null;
        }

        return new Dictionary<string, System.Windows.Input.ICommand>(StringComparer.Ordinal)
        {
            ["Open"] = _cookieConnectCommand,
            ["Close"] = CloseCookieEditorCommand,
            ["Save"] = SaveSessionCookieCommand,
            ["Clear"] = _cookieClearCommand,
            ["Unlink"] = _cookieClearCommand,
        };
    }

    private readonly CommunityToolkit.Mvvm.Input.RelayCommand _cookieConnectCommand;
    private readonly CommunityToolkit.Mvvm.Input.RelayCommand _cookieClearCommand;

    private void NotifyCookieLinkCommands()
    {
        _cookieConnectCommand.NotifyCanExecuteChanged();
        _cookieClearCommand.NotifyCanExecuteChanged();
    }

    private object? RowValueOf(SettingsDescriptor d) => d.Key switch
    {
        SettingsKeys.PollIntervalSeconds => PollIntervalSeconds,
        SettingsKeys.MaxRequestsPerMinute => RequestsPerMinute,
        SettingsKeys.MaxConcurrentDetailFetches => MaxConcurrentDetailFetches,
        SettingsKeys.SafeRequests => SafeRequests,
        SettingsKeys.QueryParams => QueryParams,
        SettingsKeys.IncludeAssets => IncludeAssets,
        SettingsKeys.NotificationGroupingEnabled => NotificationGroupingEnabled,
        SettingsKeys.EnableSourceNotifications => SourceNotificationsEnabled,
        SettingsKeys.GroupingMode => GroupingMode.ToString(),
        SettingsKeys.GroupingThreshold => GroupingThreshold,
        SettingsKeys.IsDarkMode => IsDarkMode,
        SettingsKeys.StartupEnabled => StartupEnabled,
        SettingsKeys.RememberLastSource => RememberLastSource,
        SettingsKeys.CloseBehaviorRemembered => RememberCloseBehavior,
        // The close-behavior row's authority is the service the window-closing path reads, not the
        // repository: "ask every time" is a cleared flag rather than a stored action.
        SettingsKeys.CloseBehaviorAction => SelectedCloseBehaviorIndex switch
        {
            1 => "MinimizeToTray",
            2 => "Exit",
            _ => "Ask",
        },
        // A contributed (module-local) key: read straight through the repository with the CLR type
        // the descriptor declares — which is how a row the view-model has never heard of works.
        _ => d.Value switch
        {
            SettingsValueKind.Bool => _settings.Get(_source, d.Key, BoolDefaultOf(d.Key, false)),
            SettingsValueKind.Int => _settings.Get(_source, d.Key, IntDefaultOf(d.Key, 0)),
            SettingsValueKind.Enum => _settings.Get(_source, d.Key, StringDefaultOf(d.Key, string.Empty)),
            _ => _settings.Get(_source, d.Key, StringDefaultOf(d.Key, string.Empty)),
        },
    };

    private void RowWrite(SettingsDescriptor d, object? value)
    {
        switch (d.Key)
        {
            case SettingsKeys.PollIntervalSeconds: PollIntervalSeconds = ToInt(value, PollIntervalSeconds); return;
            case SettingsKeys.MaxRequestsPerMinute: RequestsPerMinute = ToInt(value, RequestsPerMinute); return;
            case SettingsKeys.MaxConcurrentDetailFetches: MaxConcurrentDetailFetches = ToInt(value, MaxConcurrentDetailFetches); return;
            case SettingsKeys.GroupingThreshold: GroupingThreshold = ToInt(value, GroupingThreshold); return;
            case SettingsKeys.GroupingMode:
                if (Enum.TryParse<NotificationGroupingMode>(value?.ToString(), out var parsedMode))
                {
                    GroupingMode = parsedMode;
                }
                return;
            case SettingsKeys.SafeRequests: SafeRequests = ToBool(value); return;
            case SettingsKeys.IncludeAssets: IncludeAssets = ToBool(value); return;
            case SettingsKeys.NotificationGroupingEnabled: NotificationGroupingEnabled = ToBool(value); return;
            case SettingsKeys.EnableSourceNotifications: SourceNotificationsEnabled = ToBool(value); return;
            case SettingsKeys.IsDarkMode: IsDarkMode = ToBool(value); return;
            case SettingsKeys.StartupEnabled: StartupEnabled = ToBool(value); return;
            case SettingsKeys.RememberLastSource: RememberLastSource = ToBool(value); return;
            case SettingsKeys.QueryParams: QueryParams = value?.ToString() ?? string.Empty; return;
            case SettingsKeys.CloseBehaviorRemembered: RememberCloseBehavior = ToBool(value); return;
            case SettingsKeys.CloseBehaviorAction:
                SelectedCloseBehaviorIndex = value?.ToString() switch
                {
                    "MinimizeToTray" => 1,
                    "Exit" => 2,
                    _ => 0,
                };
                return;
        }

        // Anything the view-model has no property for (a source's own contributed row, a future
        // setting) persists straight through the repository and validates through the shared
        // checker — the path every new setting takes from step 7 onward.
        var error = SettingsValidator.Validate(d, value);
        if (error is not null)
        {
            SetRowError(d.Key, error);
            return;
        }

        ClearRowError(d.Key);
        if (d.Scope == SettingsScope.Global)
        {
            DeferredPreferences.SetGlobal(_settings, d.Key, value);
        }
        else
        {
            DeferredPreferences.Set(_settings, _source, d.Key, value);
        }
    }

    private static int ToInt(object? value, int fallback) => value switch
    {
        int i => i,
        string s when int.TryParse(s, out var parsed) => parsed,
        _ => fallback,
    };

    private static bool ToBool(object? value) => value switch
    {
        bool b => b,
        string s when bool.TryParse(s, out var parsed) => parsed,
        _ => false,
    };

    private System.Windows.Input.ICommand? RowCommandOf(SettingsDescriptor d) => d.Shape switch
    {
        SettingsShape.ActionButton when d.CommandName == "Save" => SaveCommand,
        SettingsShape.CookieEditor => UploadSessionCookieCommand,
        _ => null,
    };

    private string? RowValueTextOf(SettingsDescriptor d) => d.Shape switch
    {
        SettingsShape.ReadOnlyStat when d.Key == "archive" => ArchiveCountText,
        SettingsShape.CookieEditor => SessionCookieStatusText,
        _ => null,
    };

    public void SetRowError(string key, string message)
    {
        if (_rowsByKey.TryGetValue(key, out var row))
        {
            row.ErrorText = message;
        }
    }

    public void ClearRowError(string key)
    {
        if (_rowsByKey.TryGetValue(key, out var row))
        {
            row.ErrorText = null;
        }
    }

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
        WorkerPool workerPool,
        NotificationGrouper grouper,
        IEnrichmentService enrichmentService,
        IProjectRepository projectRepository,
        GlobalAppStatusService globalStatus,
        ICookieStoreProvider cookieStoreProvider,
        CloseBehaviorService closeBehaviorService,
        IStartupService startupService,
        ISettingsRepository settings,
        SourceContributionRegistry contributions,
        IServiceProvider services)
    {
        _cookieStores = cookieStoreProvider;
        _pollService = pollService;
        _rateLimiter = rateLimiter;
        _workerPool = workerPool;
        _grouper = grouper;
        _enrichmentService = enrichmentService;
        _projectRepository = projectRepository;
        _globalStatus = globalStatus;
        _closeBehaviorService = closeBehaviorService;
        _startupService = startupService;
        _settings = settings;
        _contributions = contributions;
        _services = services;

        // The cookie row's links are the only controls whose visibility is a state question rather
        // than a data one; CanExecute carries the state so the renderer stays source-blind.
        _cookieConnectCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(
            () => OpenCookieEditorCommand.Execute(null), () => !HasSessionCookie);
        _cookieClearCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(
            () => ClearSessionCookieCommand.Execute(null), () => HasSessionCookie);

        // Composed before the values are read: the catalog is where defaults and bounds live, so
        // LoadFromPreferences takes its fallbacks from it (settings plugin step 4).
        ComposeGroups();
        LoadFromPreferences();
        RefreshCookieStatus();

        // Guarded the same way LoadFromPreferences guards its own initial reads: setting
        // RememberCloseBehavior here must not itself trigger OnRememberCloseBehaviorChanged
        // (which would otherwise call ForgetRememberedAction() on every VM construction whenever
        // nothing is currently remembered).
        _isLoading = true;
        RefreshCloseBehaviorStatus();
        _isLoading = false;

        _ = LoadProjectsAddedTodayAsync();
        _ = LoadArchiveCountAsync();
    }

    /// <summary>
    /// Re-points the settings surface after a source morph (the rail tiles or the header
    /// <see cref="MostaqlK.UI.PlatformComponents.SourceRail.SourceSwitchTiles"/> switched the active
    /// platform). The global projection is already applied inside the tiles unit
    /// (<see cref="MostaqlK.Services.SourceMorph.ApplyOptimistic"/>); this only repaints what reads
    /// <see cref="_source"/>: the per-source preference rows, the contributed sections, the session
    /// cookie read-out, and the descriptor-driven visibility/name/currency display properties (plain
    /// getters over <c>ActiveDescriptor</c>, so they must be re-raised explicitly). Reuses the exact
    /// private loads the constructor runs — no new mutation paths, no source-id branch.
    /// </summary>
    public void RefreshForActiveSource()
    {
        // Composed before the values are read: the catalog is where defaults and bounds live, so
        // LoadFromPreferences takes its fallbacks from it (settings plugin step 4).
        ComposeGroups();
        LoadFromPreferences();
        RefreshCookieStatus();

        foreach (var name in new[]
                 {
                     nameof(ActiveSourceName),
                     nameof(ActiveSourceGlyph),
                     nameof(ActiveSourceGlyphImage),
                     nameof(ShowsSourceGlyphLetter),
                 })
        {
            OnPropertyChanged(name);
        }

        _ = LoadProjectsAddedTodayAsync();
        _ = LoadArchiveCountAsync();
    }

    // -----------------------------------------------------------------
    // Session cookie ("ملف الجلسة")
    // -----------------------------------------------------------------

    /// <summary>
    /// The session store for the LIVE active source (v3 morph). The cookie card's copy already
    /// follows <see cref="_source"/>; the operations now do too, so switching the rail makes the
    /// card manage the other source's session while each store stays bound to its own descriptor's
    /// keys/jar (Mostaql keeps <c>cookie_mostaql</c> and its legacy migration untouched).
    /// </summary>
    private CookieStore ActiveCookieStore => _cookieStores.For(_source);

    /// <summary>True once a session cookie is stored, which switches the row to its "active" copy.</summary>
    [ObservableProperty]
    public partial bool HasSessionCookie { get; set; }

    /// <summary>Human-readable state of the stored cookie, shown under the upload button.</summary>
    [ObservableProperty]
    public partial string SessionCookieStatusText { get; set; } = string.Empty;

    /// <summary>Bound to the session cookie entry text box in settings layouts.</summary>
    [ObservableProperty]
    public partial string SessionCookieInput { get; set; } = string.Empty;

    /// <summary>
    /// True while the picked file is being parsed and encrypted. Drives the drop zone's spinner
    /// and disables the command, so the (short but non-instant) DPAPI round-trip is visible
    /// feedback rather than an unexplained frozen frame.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UploadSessionCookieCommand))]
    [NotifyCanExecuteChangedFor(nameof(ClearSessionCookieCommand))]
    public partial bool IsCookieBusy { get; set; }

    /// <summary>Transient "saved" state of the drop zone (green outline + check).</summary>
    [ObservableProperty]
    public partial bool IsCookieSuccess { get; set; }

    /// <summary>Transient "rejected" state of the drop zone (red outline + cross).</summary>
    [ObservableProperty]
    public partial bool IsCookieError { get; set; }

    /// <summary>Result line under the drop zone; empty while idle.</summary>
    [ObservableProperty]
    public partial string CookieFeedbackText { get; set; } = string.Empty;

    /// <summary>The upload affordance is inert while a previous upload is still being encrypted.</summary>
    public bool IsCookieIdle => !IsCookieBusy;

    partial void OnIsCookieBusyChanged(bool value) => OnPropertyChanged(nameof(IsCookieIdle));

    /// <summary>
    /// Only true in development builds, where this source's cookie jar still accepts a plaintext
    /// <c>cookies.txt</c> next to the repo / an env var. A shipped build reads the cookie solely
    /// from the encrypted store, so this note must not appear there.
    /// </summary>
    public bool ShowDevelopmentCookieNote => ActiveCookieStore.DevelopmentFallbacksEnabled;

    private void RefreshCookieStatus()
    {
        HasSessionCookie = ActiveCookieStore.HasCookie;
        SessionCookieStatusText = ActiveCookieStore.HasCookie
            ? $"جلسة محفوظة ({ActiveCookieStore.CookieCount} كوكيز) - آخر تحديث {ActiveCookieStore.UpdatedAtUtc?.ToLocalTime():yyyy/MM/dd HH:mm}"
            : "لا توجد جلسة محفوظة. بدون ملف الجلسة لن يتمكن التطبيق من تنزيل مرفقات المشاريع.";
    }

    /// <summary>
    /// Lets the user pick a browser-exported cookie file and stores it encrypted in the local
    /// database. Only the parsed header is kept - the picked file itself is never copied anywhere.
    /// </summary>
    [TraceInteraction("UploadSessionCookieCommand")]
    [MostaqlK.Core.ErrorOutcome(MostaqlK.Core.ErrorOutcome.Handled, Label = "Picker/IO failure surfaced as a validation message")]
    [RelayCommand(CanExecute = nameof(IsCookieIdle))]
    public async Task UploadSessionCookieAsync()
    {
        using var _ = TraceScope.Begin("UploadSessionCookieCommand");
        try
        {
            var picked = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "اختر ملف الجلسة (cookies.txt)",
            });

            if (picked is null)
            {
                return;
            }

            IsCookieSuccess = false;
            IsCookieError = false;
            IsCookieBusy = true;
            CookieFeedbackText = "جارٍ التحقق من الملف وتشفيره...";

            var content = await File.ReadAllTextAsync(picked.FullPath);

            // Parsing + DPAPI encryption + the SQLite write take a few hundred milliseconds; keep
            // the spinner on screen for at least that long so the state change reads as a
            // deliberate step rather than a flicker.
            var save = ActiveCookieStore.SaveFromFileContentAsync(content);
            await Task.WhenAll(save, Task.Delay(SpinnerMinimumVisibleMilliseconds));
            var count = save.Result;

            IsCookieBusy = false;

            if (count is null)
            {
                IsCookieError = true;
                CookieFeedbackText = "تعذّر قراءة أي كوكيز صالحة من الملف المحدد.";
                SetValidationError("تعذّر قراءة أي كوكيز صالحة من الملف المحدد.");
                return;
            }

            IsCookieSuccess = true;
            CookieFeedbackText = $"تم حفظ {count} كوكيز مُشفّرة بنجاح.";
            ClearValidationError();
            RefreshCookieStatus();
        }
        catch (Exception ex)
        {
            _.MarkFaulted(ex);
            IsCookieBusy = false;
            IsCookieError = true;
            CookieFeedbackText = "تعذّر حفظ ملف الجلسة.";
            SetValidationError("تعذّر حفظ ملف الجلسة.");
        }
    }

    /// <summary>Deletes the stored session so the app goes back to anonymous scraping.</summary>
    [TraceInteraction("ClearSessionCookieCommand")]
    [MostaqlK.Core.ErrorOutcome(MostaqlK.Core.ErrorOutcome.Handled, Label = "Delete failure logged inside CookieStore")]
    [RelayCommand(CanExecute = nameof(IsCookieIdle))]
    public async Task ClearSessionCookieAsync()
    {
        using var _ = TraceScope.Begin("ClearSessionCookieCommand");

        // Destructive action — ask first. Secondary = "مسح" (proceed); Primary/dismiss = cancel.
        var nativeWindow = ConfirmationBox.TryGetActiveNativeWindow();
        if (nativeWindow is not null)
        {
            var confirm = await ConfirmationBox.ShowAsync(
                nativeWindow,
                title: "مسح الجلسة",
                message: "سيتم مسح بيانات الجلسة المخزنة. هل تريد المتابعة؟",
                primaryText: "إلغاء",
                secondaryText: "مسح");

            if (!confirm.IsSecondary)
            {
                return;
            }
        }

        await ActiveCookieStore.ClearAsync();
        IsCookieSuccess = false;
        IsCookieError = false;
        CookieFeedbackText = string.Empty;
        SessionCookieInput = string.Empty;
        ClearValidationError();
        RefreshCookieStatus();
    }

    /// <summary>
    /// Saves the raw cookie string entered in the text box.
    /// </summary>
    [RelayCommand(CanExecute = nameof(IsCookieIdle))]
    public async Task SaveSessionCookieAsync()
    {
        if (string.IsNullOrWhiteSpace(SessionCookieInput))
        {
            SetValidationError("يرجى إدخال قيمة الكوكي.");
            return;
        }

        try
        {
            IsCookieBusy = true;
            var content = SessionCookieInput.Trim();
            // If just a raw value or header was entered, format appropriately for saving — the
            // session cookie NAME is the source's own fact, taken from its descriptor.
            var sessionName = MostaqlK.Core.Sources.SourceRegistryProvider.DescriptorFor(_source)?.SessionCookieName;
            if (sessionName is not null && !content.Contains(sessionName) && !content.Contains("Cookie:"))
            {
                content = $"{sessionName}={content}";
            }

            var save = ActiveCookieStore.SaveFromFileContentAsync(content);
            await Task.WhenAll(save, Task.Delay(SpinnerMinimumVisibleMilliseconds));
            var count = save.Result;

            IsCookieBusy = false;

            if (count is null || count == 0)
            {
                IsCookieError = true;
                SetValidationError("تعذّر قراءة كوكي صالح من النص المدخل.");
                return;
            }

            IsCookieSuccess = true;
            SessionCookieInput = string.Empty;
            ClearValidationError();
            RefreshCookieStatus();
        }
        catch (Exception)
        {
            IsCookieBusy = false;
            IsCookieError = true;
            SetValidationError("تعذّر حفظ الكوكي.");
        }
    }

    // -----------------------------------------------------------------
    // "جلسة الإثراء" inline editor (v3 settings row)
    // -----------------------------------------------------------------

    /// <summary>Opens the compact cookie editor behind the session row's "ربط كوكيز" link.</summary>
    [RelayCommand]
    private void OpenCookieEditor() => IsCookieEditorOpen = true;

    /// <summary>Closes the editor back to the row's compact value + "ربط/إلغاء الربط" link.</summary>
    [RelayCommand]
    private void CloseCookieEditor()
    {
        IsCookieEditorOpen = false;
        SessionCookieInput = string.Empty;
    }

    // -----------------------------------------------------------------
    // Close-to-tray confirmation ("إغلاق التطبيق" / CloseBehaviorService)
    // -----------------------------------------------------------------

    /// <summary>
    /// True once the user has ticked "remember my choice" on the X-button confirmation dialog
    /// (see <see cref="CloseBehaviorService"/>), so every later click silently repeats the same
    /// action without asking again. Toggling this row off is the settings-side equivalent of the
    /// standalone <c>tools/reset-close-behavior-preference.ps1</c> script.
    /// </summary>
    [ObservableProperty]
    public partial bool RememberCloseBehavior { get; set; }

    /// <summary>Human-readable summary of the remembered choice, or that none is remembered.</summary>
    [ObservableProperty]
    public partial string CloseBehaviorStatusText { get; set; } = string.Empty;

    /// <summary>
    /// Index corresponding to: 0 = Ask every time, 1 = Minimize to tray, 2 = Exit.
    /// Bound to the CloseBehavior Picker in settings layouts.
    /// </summary>
    [ObservableProperty]
    public partial int SelectedCloseBehaviorIndex { get; set; }

    private void RefreshCloseBehaviorStatus()
    {
        var remembered = _closeBehaviorService.GetRememberedAction();
        RememberCloseBehavior = remembered is not null;
        SelectedCloseBehaviorIndex = remembered switch
        {
            CloseAction.MinimizeToTray => 1,
            CloseAction.Exit => 2,
            _ => 0
        };
        CloseBehaviorStatusText = remembered switch
        {
            CloseAction.Exit => "سيتم إغلاق التطبيق نهائيًا عند الضغط على X دون سؤالك مرة أخرى.",
            CloseAction.MinimizeToTray => "سيستمر التطبيق بالعمل في الخلفية عند الضغط على X دون سؤالك مرة أخرى.",
            _ => "سيُسألك التطبيق عن السلوك المطلوب في كل مرة تضغط فيها على X.",
        };
    }

    partial void OnSelectedCloseBehaviorIndexChanged(int value)
    {
        if (_isLoading)
        {
            return;
        }

        switch (value)
        {
            case 1:
                _closeBehaviorService.RememberAction(CloseAction.MinimizeToTray);
                break;
            case 2:
                _closeBehaviorService.RememberAction(CloseAction.Exit);
                break;
            default:
                _closeBehaviorService.ForgetRememberedAction();
                break;
        }

        RefreshCloseBehaviorStatus();
    }

    partial void OnRememberCloseBehaviorChanged(bool value)
    {
        if (_isLoading)
        {
            return;
        }

        if (!value)
        {
            // Turning the toggle off is the only direction settable from here - it forgets the
            // remembered choice so the confirmation dialog is shown again next time. There is no
            // stored choice to "remember" from this row alone (that only happens via the dialog's
            // own checkbox), so flipping it back on would have nothing to persist.
            _closeBehaviorService.ForgetRememberedAction();
        }

        RefreshCloseBehaviorStatus();
    }

    private void LoadFromPreferences()
    {
        _isLoading = true;

        // Every fallback is the descriptor's own default (resolved through the composed catalog), so
        // an untouched install agrees with the service that consumes the value — the disagreement
        // this replace removes was: screen said 60s, PollService said 30; screen said parallel max
        // 16, WorkerPool enforces none; screen said threshold 5, catalog said 3.
        PollIntervalSeconds = _settings.Get(_source, SettingsKeys.PollIntervalSeconds, IntDefaultOf(SettingsKeys.PollIntervalSeconds, 30));
        RequestsPerMinute = _settings.Get(_source, SettingsKeys.MaxRequestsPerMinute, IntDefaultOf(SettingsKeys.MaxRequestsPerMinute, 2));
        MaxConcurrentDetailFetches = _settings.Get(_source, SettingsKeys.MaxConcurrentDetailFetches, IntDefaultOf(SettingsKeys.MaxConcurrentDetailFetches, 2));
        QueryParams = _settings.Get(_source, SettingsKeys.QueryParams, StringDefaultOf(SettingsKeys.QueryParams, string.Empty));
        IncludeAssets = _settings.Get(_source, SettingsKeys.IncludeAssets, BoolDefaultOf(SettingsKeys.IncludeAssets, false));
        NotificationGroupingEnabled = _settings.Get(_source, SettingsKeys.NotificationGroupingEnabled, BoolDefaultOf(SettingsKeys.NotificationGroupingEnabled, false));
        SourceNotificationsEnabled = _settings.Get(_source, SettingsKeys.EnableSourceNotifications, BoolDefaultOf(SettingsKeys.EnableSourceNotifications, true));
        SafeRequests = _settings.Get(_source, SettingsKeys.SafeRequests, BoolDefaultOf(SettingsKeys.SafeRequests, true));
        StartupEnabled = _settings.GetGlobal(SettingsKeys.StartupEnabled, false);
        RememberLastSource = _settings.GetGlobal(SettingsKeys.RememberLastSource, true);
        GroupingThreshold = _settings.Get(_source, SettingsKeys.GroupingThreshold, IntDefaultOf(SettingsKeys.GroupingThreshold, 5));
        // Seed from the theme the app already resolved at startup (App.xaml.cs, which honours a
        // `--theme=light|dark` argument over the stored preference) and only fall back to the
        // preference / effective theme when no explicit theme was applied. Reading the preference unconditionally
        // here would silently undo that startup override the moment this page is constructed.
        IsDarkMode = Application.Current?.UserAppTheme switch
        {
            AppTheme.Dark => true,
            AppTheme.Light => false,
            _ => _settings.ContainsGlobal(SettingsKeys.IsDarkMode)
                ? _settings.GetGlobal(SettingsKeys.IsDarkMode, false)
                : Application.Current?.RequestedTheme == AppTheme.Dark,
        };

        var storedMode = _settings.Get(_source, SettingsKeys.GroupingMode, nameof(NotificationGroupingMode.EndOfMinute));
        GroupingMode = Enum.TryParse<NotificationGroupingMode>(storedMode, out var parsedMode)
            ? parsedMode
            : NotificationGroupingMode.EndOfMinute;

        _isLoading = false;

        // The Picker bridges the enum as an index; refresh it once the mode is seeded so the
        // control shows the stored row on first paint (same guarded pattern as every other load).

        // Apply the loaded values to the live pipeline services right away, so the running
        // instances always reflect whatever was last persisted (e.g. after an app restart).
        ApplyPollSettings();
        ApplyWorkerSettings();
        ApplyGroupingSettings();
        ApplyAssetSettings();
    }

    private async Task LoadProjectsAddedTodayAsync()
    {
        var result = await _projectRepository.CountAddedTodayAsync(_source);
        if (!result.IsError)
        {
            _globalStatus.SetProjectsAddedToday(result.Value);
        }
    }

    /// <summary>Loads the active source's total stored-project count for the "الأرشيف" row.</summary>
    private async Task LoadArchiveCountAsync()
    {
        var result = await _projectRepository.CountTrackedAsync(_source);
        if (!result.IsError)
        {
            ArchiveCount = result.Value.Tracked;
        }
    }

    partial void OnPollIntervalSecondsChanged(int value)
    {
        if (_isLoading)
        {
            return;
        }

        if (!TryValidate(SettingsKeys.PollIntervalSeconds, value))
        {
            return;
        }

        DeferredPreferences.Set(_settings, _source, SettingsKeys.PollIntervalSeconds, value);
        ApplyPollSettings();
    }

    partial void OnRequestsPerMinuteChanged(int value)
    {
        if (_isLoading)
        {
            return;
        }

        if (!TryValidate(SettingsKeys.MaxRequestsPerMinute, value))
        {
            return;
        }

        DeferredPreferences.Set(_settings, _source, SettingsKeys.MaxRequestsPerMinute, value);
        ApplyPollSettings();
    }

    partial void OnMaxConcurrentDetailFetchesChanged(int value)
    {
        if (_isLoading) return;
        if (!TryValidate(SettingsKeys.MaxConcurrentDetailFetches, value))
        {
            return;
        }

        DeferredPreferences.Set(_settings, _source, SettingsKeys.MaxConcurrentDetailFetches, value);
        ApplyWorkerSettings();
    }

    partial void OnQueryParamsChanged(string value)
    {
        if (_isLoading) return;
        DeferredPreferences.Set(_settings, _source, SettingsKeys.QueryParams, value ?? string.Empty);
        ApplyPollSettings();
    }

    partial void OnIncludeAssetsChanged(bool value)
    {
        if (_isLoading) return;
        DeferredPreferences.Set(_settings, _source, SettingsKeys.IncludeAssets, value);
        ApplyAssetSettings();
    }

    partial void OnNotificationGroupingEnabledChanged(bool value)
    {
        if (_isLoading) return;
        DeferredPreferences.Set(_settings, _source, SettingsKeys.NotificationGroupingEnabled, value);
        ApplyGroupingSettings();
    }

    partial void OnSafeRequestsChanged(bool value)
    {
        if (_isLoading)
        {
            return;
        }

        DeferredPreferences.Set(_settings, _source, SettingsKeys.SafeRequests, value);
        ApplyPollSettings();
    }

    partial void OnStartupEnabledChanged(bool value)
    {
        if (_isLoading)
        {
            return;
        }

        DeferredPreferences.SetGlobal(_settings, SettingsKeys.StartupEnabled, value);
        _startupService.SetStartupEnabled(value);
    }

    partial void OnGroupingModeChanged(NotificationGroupingMode value)
    {

        if (_isLoading)
        {
            return;
        }

        DeferredPreferences.Set(_settings, _source, SettingsKeys.GroupingMode, value.ToString());
        ApplyGroupingSettings();
    }

    partial void OnGroupingThresholdChanged(int value)
    {
        if (_isLoading)
        {
            return;
        }

        // EndOfMinute ignores the threshold, so a non-positive value is only invalid for the
        // modes that actually consume it (AfterMinutes / AfterCount). This cross-key condition is
        // the one rule a descriptor cannot state on its own (it depends on ANOTHER row's value),
        // so it stays here explicitly rather than being smuggled into the contract as a flag.
        if (GroupingMode != NotificationGroupingMode.EndOfMinute
            && !TryValidate(SettingsKeys.GroupingThreshold, value))
        {
            return;
        }

        ClearValidationError();
        DeferredPreferences.Set(_settings, _source, SettingsKeys.GroupingThreshold, value);
        ApplyGroupingSettings();
    }

    partial void OnIsDarkModeChanged(bool value)
    {
        if (_isLoading)
        {
            return;
        }

        DeferredPreferences.SetGlobal(_settings, SettingsKeys.IsDarkMode, value);
        ApplyTheme();
    }

    partial void OnRememberLastSourceChanged(bool value)
    {
        if (_isLoading)
        {
            return;
        }

        // Global (not per-source) like dark mode / startup — the same single mutation path. The
        // restore consumer lives outside this VM (parent-wired at startup); here we only persist.
        DeferredPreferences.SetGlobal(_settings, SettingsKeys.RememberLastSource, value);
    }

    partial void OnSourceNotificationsEnabledChanged(bool value)
    {
        if (_isLoading)
        {
            return;
        }

        // Per-source, namespaced container — every other pipeline knob uses the same path.
        DeferredPreferences.Set(_settings, _source, SettingsKeys.EnableSourceNotifications, value);
    }

    private void ApplyPollSettings()
    {
        _pollService.PollIntervalSeconds = PollIntervalSeconds;
        _pollService.QueryParams = QueryParams;

        // The limiter derives capacity, refill and spacing from these two values itself, so the
        // numbers can no longer drift apart from the configured budget.
        _rateLimiter.Reconfigure(RequestsPerMinute, SafeRequests);
            }

    private void ApplyGroupingSettings()
    {
        _grouper.Mode = GroupingMode;
        _grouper.Enabled = NotificationGroupingEnabled;

        if (GroupingMode == NotificationGroupingMode.AfterMinutes)
        {
            _grouper.AfterMinutesThreshold = Math.Max(1, GroupingThreshold);
        }
        else if (GroupingMode == NotificationGroupingMode.AfterCount)
        {
            _grouper.AfterCountThreshold = Math.Max(1, GroupingThreshold);
        }
    }

    private void ApplyWorkerSettings()
    {
        _workerPool.Reconfigure(MaxConcurrentDetailFetches);
    }

    private void ApplyAssetSettings()
    {
        _enrichmentService.DownloadAssets = IncludeAssets;
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

    /// <summary>
    /// The one validation entry point for a numeric row: the rule (bounds + message) is read off
    /// the setting's descriptor by <see cref="SettingsValidator"/>, so a source that overrides
    /// `Min`/`Max`/`ErrorAr` changes what is accepted without any code knowing it exists. The
    /// offending message is shown both in the page-level banner (historic) and on the row itself
    /// (what the catalog-driven renderer uses).
    /// </summary>
    private bool TryValidate(string key, int value)
    {
        var descriptor = DescriptorOf(key);
        var error = descriptor is null ? null : SettingsValidator.Validate(descriptor, value);

        if (error is not null)
        {
            SetValidationError(error);
            SetRowError(key, error);
            return false;
        }

        ClearValidationError();
        ClearRowError(key);
        return true;
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
            // Every field already applies live on change and persists through the DEFERRED
            // last-wins queue (v3 frame decoupling: keystroke-rate edits never block-write the
            // settings store on the UI thread). Save = confirm nothing is pending: drain the
            // queue so the click guarantees durability before any teardown.
            return DeferredPreferences.FlushAsync();
        }
        catch (Exception ex)
        {
            _.MarkFaulted(ex);
            throw;
        }
    }
}
