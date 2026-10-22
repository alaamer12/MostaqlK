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

    // configuration-reference.md: `poll_interval_seconds` default 30, `max_requests_per_minute`
    // default 2. This screen used to advertise a 60s default while PollService itself defaulted to
    // 30, so the two disagreed on an untouched install.
    private const int DefaultPollIntervalSeconds = 30;
    private const int DefaultMaxRequestsPerMinute = TokenBucketRateLimiter.DefaultRequestsPerMinute;
    private const int MinPollIntervalSeconds = 10;
    private const int MaxPollIntervalSeconds = 3600;

    /// <summary>Floor for how long the cookie drop zone's spinner stays visible, in milliseconds.</summary>
    private const int SpinnerMinimumVisibleMilliseconds = 500;

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
    [NotifyPropertyChangedFor(nameof(SafeRequestsHintText))]
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
    [NotifyPropertyChangedFor(nameof(SafeRequestsHintText))]
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
    [NotifyPropertyChangedFor(nameof(IsCookieEditorClosed))]
    public partial bool IsCookieEditorOpen { get; set; }

    /// <summary>True while the cookie editor is hidden (drives the row's compact value+link display).</summary>
    public bool IsCookieEditorClosed => !IsCookieEditorOpen;

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

    /// <summary>
    /// "جلسة الإثراء" row shows only when the source's descriptor marks session-gated enrichment.
    /// A source that enriches fully without a session renders no row (the module's own contributed
    /// session read-out, if any, comes through <see cref="ContributedSections"/> instead).
    /// </summary>
    public bool ShowSessionEnrichmentRow => ActiveDescriptor?.Capabilities.NeedsSessionForFullEnrichment ?? false;

    /// <summary>
    /// "أنواع المشاريع المتابَعة" row shows only when the source declares a supervised-projects
    /// capability. No registered source sets it true today, so the row is honestly absent until one
    /// does — the same presence-by-data rule as the details schema (contract §1).
    /// </summary>
    public bool ShowSupervisedTypesRow => ActiveDescriptor?.Capabilities.HasSupervisedFlag ?? false;

    /// <summary>
    /// "عملة الميزانية" display row shows only when the source's currency differs from the default
    /// source's (i.e. a non-ر.س platform) — the mockup renders it for the $-priced source only.
    /// </summary>
    public bool ShowBudgetCurrencyRow
    {
        get
        {
            var active = ActiveDescriptor?.CurrencySuffix;
            if (string.IsNullOrEmpty(active))
            {
                return false;
            }

            var defaultSuffix = MostaqlK.Core.Sources.SourceRegistryProvider
                .DescriptorFor(MostaqlK.Core.Sources.SourceId.Default)?.CurrencySuffix;
            return !string.Equals(active, defaultSuffix, StringComparison.Ordinal);
        }
    }

    /// <summary>The budget-currency value text, e.g. "كما في الموقع ($)" — suffix straight from the descriptor.</summary>
    public string BudgetCurrencyDisplayText => $"كما في الموقع ({ActiveDescriptor?.CurrencySuffix ?? string.Empty})";

    /// <summary>Hint under the session row; the platform name is the descriptor's, never a literal.</summary>
    public string SessionEnrichmentHint =>
        $"بعض صفحات {ActiveSourceName} تتطلب جلسة للدخول — بدونها يُضاف المشروع من الملخص فقط";

    /// <summary>Session row's state word: "مرتبطة" when a cookie is stored, else "غير مرتبطة".</summary>
    public string SessionValueText => HasSessionCookie ? "مرتبطة" : "غير مرتبطة";

    partial void OnHasSessionCookieChanged(bool value) => OnPropertyChanged(nameof(SessionValueText));

    // Grouping-mode Picker bridge: the UI tests drive a SelectedIndex on `Settings_GroupingModePicker`,
    // so the enum is exposed as an index over the three Arabic labels in <see cref="GroupingModeOptions"/>.
    /// <summary>Arabic grouping-mode labels in enum order (EndOfMinute / AfterMinutes / AfterCount).</summary>
    public IReadOnlyList<string> GroupingModeOptions { get; } =
        ["نهاية الدقيقة", "بعد دقائق", "بعد عدد"];

    /// <summary>Two-way Picker index mapped onto <see cref="GroupingMode"/>.</summary>
    public int GroupingModeIndex
    {
        get => (int)GroupingMode;
        set
        {
            if (value >= 0 && value < GroupingModeOptions.Count && (NotificationGroupingMode)value != GroupingMode)
            {
                GroupingMode = (NotificationGroupingMode)value;
            }
        }
    }

    public GlobalAppStatusService GlobalStatus => _globalStatus;

    /// <summary>
    /// Sections the ACTIVE source's module contributes beyond the shared descriptor rows (contract
    /// §4/§10). Built from <see cref="SourceContributionRegistry.SettingsSectionsFor"/> — never an
    /// <c>if (source == X)</c>. A source with no contribution (mostaql today) yields an empty list, so
    /// <see cref="HasContributedSections"/> is false and the host renders nothing for it (absent =
    /// never rendered, not empty). Each entry is the module's own bindable view-model.
    /// </summary>
    public IReadOnlyList<ISourceSettingsSectionViewModel> ContributedSections { get; private set; } = [];

    /// <summary>Drives the host's section-group visibility.</summary>
    public bool HasContributedSections => ContributedSections.Count > 0;

    private void LoadContributedSections()
    {
        ContributedSections = _contributions.SettingsSectionsFor(_source)
            .Select(contribution => contribution.CreateViewModel(_services))
            .OfType<ISourceSettingsSectionViewModel>()
            .ToList();
        OnPropertyChanged(nameof(ContributedSections));
        OnPropertyChanged(nameof(HasContributedSections));
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

        LoadFromPreferences();
        LoadContributedSections();
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

        PollIntervalSeconds = _settings.Get(_source, SettingsKeys.PollIntervalSeconds, DefaultPollIntervalSeconds);
        RequestsPerMinute = _settings.Get(_source, SettingsKeys.MaxRequestsPerMinute, DefaultMaxRequestsPerMinute);
        MaxConcurrentDetailFetches = _settings.Get(_source, SettingsKeys.MaxConcurrentDetailFetches, 2);
        QueryParams = _settings.Get(_source, SettingsKeys.QueryParams, string.Empty);
        IncludeAssets = _settings.Get(_source, SettingsKeys.IncludeAssets, false);
        NotificationGroupingEnabled = _settings.Get(_source, SettingsKeys.NotificationGroupingEnabled, false);
        SourceNotificationsEnabled = _settings.Get(_source, SettingsKeys.EnableSourceNotifications, true);
        SafeRequests = _settings.Get(_source, SettingsKeys.SafeRequests, true);
        StartupEnabled = _settings.GetGlobal(SettingsKeys.StartupEnabled, false);
        RememberLastSource = _settings.GetGlobal(SettingsKeys.RememberLastSource, true);
        GroupingThreshold = _settings.Get(_source, SettingsKeys.GroupingThreshold, 5);
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
        OnPropertyChanged(nameof(GroupingModeIndex));

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

        if (!TryValidateRange(
                value,
                MinPollIntervalSeconds,
                MaxPollIntervalSeconds,
                $"فترة الفحص يجب أن تكون بين {MinPollIntervalSeconds} و {MaxPollIntervalSeconds} ثانية."))
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

        if (!TryValidateRange(value, min: 1, max: int.MaxValue, "الحد الأقصى للطلبات يجب أن يكون رقمًا أكبر من صفر."))
        {
            return;
        }

        DeferredPreferences.Set(_settings, _source, SettingsKeys.MaxRequestsPerMinute, value);
        ApplyPollSettings();
    }

    partial void OnMaxConcurrentDetailFetchesChanged(int value)
    {
        if (_isLoading) return;
        if (!TryValidateRange(value, min: 1, max: int.MaxValue, "عدد التوازي يجب أن يكون رقمًا أكبر من صفر."))
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
        OnPropertyChanged(nameof(GroupingModeIndex));

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
        // modes that actually consume it (AfterMinutes / AfterCount).
        if (GroupingMode != NotificationGroupingMode.EndOfMinute
            && !TryValidateRange(value, min: 1, max: int.MaxValue, "حد التجميع يجب أن يكون رقمًا أكبر من صفر."))
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
        OnPropertyChanged(nameof(SafeRequestsHintText));
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
    /// Shared range-check used by the numeric settings <c>OnXChanged</c> handlers. Sets the
    /// validation error and returns <c>false</c> when <paramref name="value"/> is outside
    /// <paramref name="min"/>..<paramref name="max"/> (inclusive); otherwise clears any prior
    /// validation error and returns <c>true</c> so the caller can persist/apply the value.
    /// </summary>
    private bool TryValidateRange(int value, int min, int max, string errorMessage)
    {
        if (value < min || value > max)
        {
            SetValidationError(errorMessage);
            return false;
        }

        ClearValidationError();
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
