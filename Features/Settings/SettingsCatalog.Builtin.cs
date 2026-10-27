using MostaqlK.Core.Sources;

namespace MostaqlK.Features.Settings;

/// <summary>
/// The base layer (<c>{}</c>): every settings row the app ships with, as data. Labels/hints/errors
/// are the exact Arabic the hardcoded layouts used, so the composed page reads identically;
/// sources override fields (or add rows) through <see cref="ISourceSettingsContribution"/>.
/// Adding a setting = one entry here; removing one = deleting it (stale overrides are dropped).
/// <para>
/// The list is grouped by band in the order the page shows them — النظام (one value shared by every
/// platform, stored flat), then المنصة (a property of one site, stored in that platform's container),
/// then الإجراءات — because band membership is the one thing a reader must be able to check by eye.
/// Rendering order itself comes from the data (a header's <c>Order</c> ranks its band, every other
/// row's <c>Order</c> positions it inside its band), so re-grouping the file changes no behavior.
/// </para>
/// </summary>
public static partial class SettingsCatalog
{
    public static IReadOnlyList<SettingsDescriptor> Builtin { get; } =
    [
        // ── Band: system (النظام) — one value, shared by every platform, flat container ───
        new("section-system", SettingsShape.SectionHeader, SettingsScope.Global,
            "النظام", "system", 0)
        {
            HintAr = "إعدادات تطبّق على كل المنصات — قيمة واحدة يشترك فيها كل ما هو مفعّل منها.",
            IconName = "Sliders",
        },
        // A system row cannot take a per-platform default: the value it falls back to has to be the
        // same number whichever platform the user is on. `ISourceDescriptor.DefaultPollIntervalSeconds`
        // (mostaql 30 / nafezly 60) went out with that line because its only consumers were this row
        // and the pipeline seed that read it; PollService's own default is 30 and
        // TestSettingsDefaultsAgreeWithServices pins the two together. What the per-platform number
        // was really protecting survives as a FLOOR instead
        // (ISourceDescriptor.MinSafePollIntervalSeconds), because a shared cadence must be able to
        // make one site faster without making another site's rate limiter angry.
        new(SettingsKeys.PollIntervalSeconds, SettingsShape.NumericBounded, SettingsScope.Global,
            "فاصل الفحص", "system", 10)
        {
            HintAr = "كل كم ثانية يُفحص البحث عن مشاريع جديدة",
            UnitAr = "ثانية",
            Min = 10,
            Max = 3600,
            Value = SettingsValueKind.Int,
            Validation = SettingsValidationKind.IntRange,
            ErrorAr = "فترة الفحص يجب أن تكون بين {0} و {1} ثانية.",
            DefaultLiteral = "30",
            BareEditor = true,
            SecondaryLabelAr = "تغيير",
            AutomationId = "Settings_PollIntervalInput",
            AutomationIdMobile = "Settings_Mobile_PollIntervalEntry",
            IsLtr = true,
        },
        // A bool, not a count: `TokenBucketRateLimiter.Reconfigure(rpm, safeRequests)` takes a flag,
        // and the view-model has always read this key as one. The rate ceiling is the platform row
        // below; this switch only chooses how strictly it is spaced, everywhere.
        new(SettingsKeys.SafeRequests, SettingsShape.Toggle, SettingsScope.Global,
            "الطلبات الآمنة", "system", 20)
        {
            HintAr = "يوزّع الطلبات على مدار الدقيقة مع فاصل ثانية بين كل طلبين — السلوك الموصى به لتجنّب الحجب",
            Value = SettingsValueKind.Bool,
            DefaultLiteral = "true",
            AutomationId = "Settings_SafeRequestsToggle",
        },
        new(SettingsKeys.MaxConcurrentDetailFetches, SettingsShape.NumericBounded, SettingsScope.Global,
            "تحميل التفاصيل المتوازي", "system", 30)
        {
            HintAr = "عدد صفحات التفاصيل التي تُجلب في وقت واحد — لكل منصة من منصاتها العاملة",
            // WorkerPool.Reconfigure has no upper bound (it only rejects <= 0), so the row must not
            // invent one: a max here would silently disagree with what the pool accepts.
            Min = 1,
            Value = SettingsValueKind.Int,
            Validation = SettingsValidationKind.IntMin,
            ErrorAr = "عدد التوازي يجب أن يكون رقمًا أكبر من صفر.",
            // The untouched-install value is what the view-model has always read (2); the pool starts
            // at that same count, so an install that never opened Settings keeps the same behavior.
            DefaultLiteral = "2",
            BareEditor = true,
            AutomationId = "Settings_ConcurrentFetchesInput",
        },
        new(SettingsKeys.NotificationGroupingEnabled, SettingsShape.Toggle, SettingsScope.Global,
            "تجميع التنبيهات", "system", 40)
        {
            HintAr = "تنبيه واحد لكل دفعة بدل تنبيه لكل مشروع — على كل المنصات",
            Value = SettingsValueKind.Bool,
            DefaultLiteral = "true",
            AutomationId = "Settings_GroupingToggle",
            AutomationIdMobile = "Settings_Mobile_GroupingToggle",
        },
        new(SettingsKeys.GroupingMode, SettingsShape.EnumPicker, SettingsScope.Global,
            "نمط التجميع", "system", 50)
        {
            // Option values are the enum NAMES as plain strings, because that is what the repository
            // already holds on every existing install (`nameof(NotificationGroupingMode.…)`);
            // indices would silently re-read a stored "AfterCount" as unknown the day this row drove
            // the write. Strings (not `nameof`) also keep this file linkable into the offline test
            // harness, which does not compile Services.
            Options =
            [
                new("EndOfMinute", "نهاية الدقيقة"),
                new("AfterMinutes", "بعد دقائق"),
                new("AfterCount", "بعد عدد"),
            ],
            Value = SettingsValueKind.Enum,
            Validation = SettingsValidationKind.EnumIndex,
            ErrorAr = "اختر نمط تجميع معروفًا.",
            DefaultLiteral = "EndOfMinute",
            AutomationId = "Settings_GroupingModePicker",
        },
        new(SettingsKeys.GroupingThreshold, SettingsShape.NumericBounded, SettingsScope.Global,
            "حد التجميع", "system", 60)
        {
            HintAr = "عدد المشاريع الذي يُجمَّع عنده التنبيه",
            Min = 1,
            Value = SettingsValueKind.Int,
            Validation = SettingsValidationKind.IntMin,
            ErrorAr = "حد التجميع يجب أن يكون رقمًا أكبر من صفر.",
            BareEditor = true,
            SecondaryLabelAr = "تغيير",
            // NotificationGrouper's own AfterCountThreshold default is 5; a settings row advertising
            // anything else would promise a number the grouper never uses.
            DefaultLiteral = "5",
            AutomationId = "Settings_GroupingThresholdInput",
            AutomationIdMobile = "Settings_Mobile_GroupingThresholdEntry",
            IsLtr = true,
        },
        new(SettingsKeys.StartupEnabled, SettingsShape.Toggle, SettingsScope.Global,
            "تشغيل مع النظام", "system", 70)
        {
            HintAr = "يبدأ التطبيق في الدرج دون نافذة",
            Value = SettingsValueKind.Bool,
            DefaultLiteral = "false",
            AutomationId = "Settings_StartupToggle",
        },
        new(SettingsKeys.RememberLastSource, SettingsShape.Toggle, SettingsScope.Global,
            "تذكر آخر منصة مفتوحة", "system", 80)
        {
            HintAr = "عند إعادة تشغيل التطبيق يعود لنفس المنصة التي كنت عليها",
            Value = SettingsValueKind.Bool,
            DefaultLiteral = "true",
            AutomationId = "Settings_RememberSourceToggle",
        },
        new(SettingsKeys.IsDarkMode, SettingsShape.Toggle, SettingsScope.Global,
            "الوضع الداكن", "system", 90)
        {
            HintAr = "تبديل مظهر التطبيق بين الفاتح والداكن",
            Value = SettingsValueKind.Bool,
            DefaultLiteral = "false",
            AutomationId = "Settings_DarkModeToggle",
            AutomationIdMobile = "Settings_Mobile_DarkModeToggle",
        },
        // The three choices the close dialog offers, as VALUES rather than indices: "Ask" is not a
        // stored action — it clears the remembered flag — while the other two are the real
        // `CloseAction` names the window-closing path reads. Same labels, same order, so the picker
        // renders identically; what stops existing is a row whose "1" silently disagrees with the
        // service that persists "Exit".
        new(SettingsKeys.CloseBehaviorAction, SettingsShape.EnumPicker, SettingsScope.Global,
            "عند الضغط على X", "system", 100)
        {
            HintAr = "سلوك التطبيق عند إغلاق النافذة",
            Options =
            [
                new("Ask", "سؤالني كل مرة"),
                new("Exit", "إغلاق نهائي"),
                new("MinimizeToTray", "الإغلاق إلى الدرج"),
            ],
            Value = SettingsValueKind.Enum,
            Validation = SettingsValidationKind.EnumIndex,
            ErrorAr = "اختر سلوكًا معروفًا للإغلاق.",
            DefaultLiteral = "Ask",
            AutomationId = "Settings_CloseBehaviorPicker",
        },
        new(SettingsKeys.CloseBehaviorRemembered, SettingsShape.Toggle, SettingsScope.Global,
            "تذكر خياري", "system", 110)
        {
            HintAr = "لا تسألني مرة أخرى بعد اختيار السلوك",
            Value = SettingsValueKind.Bool,
            DefaultLiteral = "false",
            // Rendered: this is how the user UNDOES a remembered close choice without hunting for
            // the dialog's own checkbox, which only appears when nothing is remembered.
            AutomationId = "Settings_RememberCloseBehaviorToggle",
        },

        // ── Band: platform (المنصة) — a property of ONE site, stored in that site's container ─
        // A SectionHeader's Order is its band's RANK in the page (SettingsCatalog reads it; the header
        // itself always renders first inside its band), so a source that contributes a section places
        // it with data instead of inheriting "whichever section appeared last".
        new("section-platform", SettingsShape.SectionHeader, SettingsScope.Global,
            "المنصة", "platform", 10)
        {
            HintAr = "المصادر يحددها المطوّر — كل منصة تعرض إعداداتها الخاصة بها، وقد تختلف الإعدادات والصفحات وحقول البطاقات من منصة لأخرى.",
            IconName = "Plug",
        },
        // Deliberately the one cadence/rate row that stays PerSource: each pipeline owns a private
        // TokenBucketRateLimiter, so a budget spent on one site has no bearing on another — the
        // number is a property of one site's tolerance, not of app behavior. (Scan interval is the
        // opposite case: one cadence for the app, floored per site by MinSafePollIntervalSeconds.)
        new(SettingsKeys.MaxRequestsPerMinute, SettingsShape.NumericBounded, SettingsScope.PerSource,
            "حد الطلبات", "platform", 20)
        {
            HintAr = "سقف لبق يحمي الحساب والموقع",
            UnitAr = "طلب/دقيقة",
            Min = 1,
            Value = SettingsValueKind.Int,
            Validation = SettingsValidationKind.IntMin,
            ErrorAr = "الحد الأقصى للطلبات يجب أن يكون رقمًا أكبر من صفر.",
            DefaultFrom = DefaultFact.RequestsPerMinute,
            BareEditor = true,
            SecondaryLabelAr = "تغيير",
            AutomationId = "Settings_RequestsPerMinuteInput",
            AutomationIdMobile = "Settings_Mobile_RateLimitEntry",
            IsLtr = true,
        },
        new(SettingsKeys.QueryParams, SettingsShape.TextEntry, SettingsScope.PerSource,
            "معاملات البحث", "platform", 50)
        {
            HintAr = "معاملات URL إضافية تُمرَّر لصفحة المشاريع في هذه المنصة (مثل: category=software-dev)",
            PlaceholderAr = "category=software-dev&sort=latest",
            Value = SettingsValueKind.String,
            IsLtr = true,
            BareEditor = true,
            AutomationId = "Settings_QueryParamsInput",
            AutomationIdMobile = "Settings_Mobile_QueryParamsEntry",
        },
        new(SettingsKeys.IncludeAssets, SettingsShape.Toggle, SettingsScope.PerSource,
            "تحميل مرفقات المشاريع", "platform", 60)
        {
            HintAr = "تنزيل صور وملفات المشروع مع التفاصيل",
            Value = SettingsValueKind.Bool,
            DefaultLiteral = "false",
            AutomationId = "Settings_IncludeAssetsToggle",
        },
        new(SettingsKeys.EnableSourceNotifications, SettingsShape.Toggle, SettingsScope.PerSource,
            "تنبيهات هذه المنصة", "platform", 70)
        {
            HintAr = "لن تصلك تنبيهات من هذه المنصة أثناء الإيقاف",
            Value = SettingsValueKind.Bool,
            DefaultLiteral = "true",
            AutomationId = "Settings_SourceNotificationsToggle",
        },
        new("supervised-types", SettingsShape.ReadOnlyStat, SettingsScope.PerSource,
            "أنواع المشاريع المتابَعة", "platform", 100)
        {
            HintAr = "هذه المنصة تتميز بمشاريع مراقبة وعادية",
            VisibilityFlag = "HasSupervisedFlag",
            CommandName = "supervised-types",
        },
        new("session-cookie", SettingsShape.CookieEditor, SettingsScope.PerSource,
            "ارفع ملف ال cookies الخاص بك", "platform", 110)
        {
            HintAr = "بدون ملف الجلسة لن يتمكن التطبيق من تنزيل مرفقات المشاريع لبعض المنصات",
            CommandName = "cookie",
            Validation = SettingsValidationKind.CookieParse,
            ErrorAr = "لم يُعثر على أي كوكيز صالحة في الملف. صدّر الملف بصيغة Netscape من متصفحك.",
            ActionLabelAr = "ربط كوكيز",
            SecondaryLabelAr = "إلغاء الربط",
            AutomationId = "Settings_OpenCookieEditorButton",
            AutomationIdMobile = "Settings_Mobile_SessionCookieEntry",
            // The revealed editor's own controls, by role: the renderer looks these up instead of
            // hardcoding ids, so the historical names the UI tests assert survive the swap.
            ControlAutomationIds = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Unlink"] = "Settings_ClearCookieButton",
                ["Save"] = "Settings_SaveCookieButton",
                ["Clear"] = "Settings_ClearCookieButton",
                ["Close"] = "Settings_CloseCookieEditorButton",
            },
        },
        new("budget-currency", SettingsShape.ReadOnlyStat, SettingsScope.PerSource,
            "عملة الميزانية", "platform", 120)
        {
            HintAr = "تُعرض ميزانيات هذه المنصة بالعملة الموضّحة في الموقع",
            VisibilityFlag = "HasCurrencySuffix",
            CommandName = "budget-currency",
        },
        new("last-scan", SettingsShape.ReadOnlyStat, SettingsScope.PerSource,
            "آخر فحص", "platform", 130)
        {
            CommandName = "last-scan",
            // The hosted LastScanStatus unit takes this as its label id (the UI tests read it).
            AutomationId = "Settings_LastScanLabel",
        },
        new("archive", SettingsShape.ReadOnlyStat, SettingsScope.PerSource,
            "الأرشيف", "platform", 140)
        {
            CommandName = "open-archive",
            AutomationId = "Settings_ViewArchiveButton",
        },

        // ── Band: actions (الإجراءات) ─────────────────────────────────────────────────────
        new("section-actions", SettingsShape.SectionHeader, SettingsScope.Global,
            "الإجراءات", "actions", 90)
        {
            IconName = "Save",
        },
        new("save", SettingsShape.ActionButton, SettingsScope.Global,
            "حفظ التغييرات", "actions", 10)
        {
            CommandName = "save",
            ActionLabelAr = "حفظ",
            AutomationId = "Settings_SaveButton",
        },
    ];

    /// <summary>
    /// The base inventory keyed by settings key, for a caller that holds a key and needs that row's
    /// <see cref="SettingsDescriptor.Scope"/> — the scope is what decides which container the value
    /// lives in (see <see cref="ScopedSettings"/>). Declared AFTER <see cref="Builtin"/> because static
    /// initializers run in textual order, and built with <c>TryAdd</c> so a duplicated key can only
    /// ever be reported by <c>TestSettingsCatalogBuiltinIntegrity</c>, never crash the settings page.
    /// </summary>
    public static IReadOnlyDictionary<string, SettingsDescriptor> BuiltinByKey { get; } = BuildIndex(Builtin);

    public static bool TryGetBuiltin(string key, out SettingsDescriptor descriptor) => BuiltinByKey.TryGetValue(key, out descriptor!);

    private static IReadOnlyDictionary<string, SettingsDescriptor> BuildIndex(IReadOnlyList<SettingsDescriptor> rows)
    {
        var index = new Dictionary<string, SettingsDescriptor>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            index.TryAdd(row.Key, row);
        }

        return index;
    }
}
