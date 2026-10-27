using MostaqlK.Core.Sources;

namespace MostaqlK.Features.Settings;

/// <summary>
/// The base layer (<c>{}</c>): every settings row the app ships with, as data. Labels/hints/errors
/// are the exact Arabic the hardcoded layouts used, so the composed page reads identically;
/// sources override fields (or add rows) through <see cref="ISourceSettingsContribution"/>.
/// Adding a setting = one entry here; removing one = deleting it (stale overrides are dropped).
/// </summary>
public static partial class SettingsCatalog
{
    public static IReadOnlyList<SettingsDescriptor> Builtin { get; } =
    [
        // ── Section: platform (المنصة) — per-source storage ─────────────────────────────
        new(SettingsKeys.PollIntervalSeconds, SettingsShape.NumericBounded, SettingsScope.PerSource,
            "فاصل الفحص", "platform", 10)
        {
            HintAr = "كل كم ثانية يُفحص البحث عن مشاريع جديدة",
            UnitAr = "ثانية",
            Min = 10,
            Max = 3600,
            Value = SettingsValueKind.Int,
            Validation = SettingsValidationKind.IntRange,
            ErrorAr = "فترة الفحص يجب أن تكون بين {0} و {1} ثانية.",
            DefaultFrom = DefaultFact.PollIntervalSeconds,
            AutomationId = "Settings_PollIntervalInput",
            AutomationIdMobile = "Settings_Mobile_PollIntervalEntry",
            IsLtr = true,
        },
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
            AutomationId = "Settings_RequestsPerMinuteInput",
            AutomationIdMobile = "Settings_Mobile_RateLimitEntry",
            IsLtr = true,
        },
        new(SettingsKeys.SafeRequests, SettingsShape.NumericBounded, SettingsScope.PerSource,
            "طلبات آمنة", "platform", 30)
        {
            HintAr = "حصة الطلبات المضمونة داخل سقف الدقيقة",
            UnitAr = "طلب",
            Min = 1,
            Value = SettingsValueKind.Int,
            Validation = SettingsValidationKind.IntMin,
            ErrorAr = "عدد الطلبات الآمنة يجب أن يكون رقمًا أكبر من صفر.",
            DefaultLiteral = "1",
            AutomationId = "Settings_SafeRequestsInput",
        },
        new(SettingsKeys.MaxConcurrentDetailFetches, SettingsShape.NumericBounded, SettingsScope.PerSource,
            "تحميل التفاصيل المتوازي", "platform", 40)
        {
            HintAr = "عدد صفحات التفاصيل التي تُجلب في وقت واحد",
            Min = 1,
            Max = 16,
            Value = SettingsValueKind.Int,
            Validation = SettingsValidationKind.IntRange,
            ErrorAr = "عدد التوازي يجب أن يكون بين {0} و {1}.",
            DefaultLiteral = "4",
            AutomationId = "Settings_ConcurrentFetchesInput",
        },
        new(SettingsKeys.QueryParams, SettingsShape.TextEntry, SettingsScope.PerSource,
            "معاملات البحث", "platform", 50)
        {
            HintAr = "معاملات URL إضافية تُمرَّر لصفحة المشاريع في هذه المنصة (مثل: category=software-dev)",
            PlaceholderAr = "category=software-dev&sort=latest",
            Value = SettingsValueKind.String,
            IsLtr = true,
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
        new(SettingsKeys.GroupingMode, SettingsShape.EnumPicker, SettingsScope.PerSource,
            "نمط التجميع", "platform", 80)
        {
            Options =
            [
                new("0", "نهاية الدقيقة"),
                new("1", "بعد دقائق"),
                new("2", "بعد عدد"),
            ],
            Value = SettingsValueKind.Enum,
            Validation = SettingsValidationKind.EnumIndex,
            ErrorAr = "اختر نمط تجميع معروفًا.",
            DefaultLiteral = "0",
            AutomationId = "Settings_GroupingModePicker",
        },
        new(SettingsKeys.GroupingThreshold, SettingsShape.NumericBounded, SettingsScope.PerSource,
            "حد التجميع", "platform", 90)
        {
            HintAr = "عدد المشاريع الذي يُجمَّع عنده التنبيه",
            Min = 1,
            Value = SettingsValueKind.Int,
            Validation = SettingsValidationKind.IntMin,
            ErrorAr = "حد التجميع يجب أن يكون رقمًا أكبر من صفر.",
            DefaultLiteral = "3",
            AutomationId = "Settings_GroupingThresholdInput",
            AutomationIdMobile = "Settings_Mobile_GroupingThresholdEntry",
            IsLtr = true,
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
            AutomationId = "Settings_OpenCookieEditorButton",
            AutomationIdMobile = "Settings_Mobile_SessionCookieEntry",
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
        },
        new("archive", SettingsShape.ReadOnlyStat, SettingsScope.PerSource,
            "الأرشيف", "platform", 140)
        {
            CommandName = "open-archive",
            AutomationId = "Settings_ViewArchiveButton",
        },

        // ── Section: general (عام) ───────────────────────────────────────────────────────
        new(SettingsKeys.StartupEnabled, SettingsShape.Toggle, SettingsScope.Global,
            "تشغيل مع النظام", "general", 10)
        {
            HintAr = "يبدأ التطبيق في الدرج دون نافذة",
            Value = SettingsValueKind.Bool,
            DefaultLiteral = "false",
            AutomationId = "Settings_StartupToggle",
        },
        new(SettingsKeys.NotificationGroupingEnabled, SettingsShape.Toggle, SettingsScope.PerSource,
            "تجميع التنبيهات", "general", 20)
        {
            HintAr = "تنبيه واحد لكل دفعة بدل تنبيه لكل مشروع — على كل المنصات",
            Value = SettingsValueKind.Bool,
            DefaultLiteral = "true",
            AutomationId = "Settings_GroupingToggle",
            AutomationIdMobile = "Settings_Mobile_GroupingToggle",
        },
        new(SettingsKeys.RememberLastSource, SettingsShape.Toggle, SettingsScope.Global,
            "تذكر آخر منصة مفتوحة", "general", 30)
        {
            HintAr = "عند إعادة تشغيل التطبيق يعود لنفس المنصة التي كنت عليها",
            Value = SettingsValueKind.Bool,
            DefaultLiteral = "true",
            AutomationId = "Settings_RememberSourceToggle",
        },
        new(SettingsKeys.IsDarkMode, SettingsShape.Toggle, SettingsScope.Global,
            "الوضع الداكن", "general", 40)
        {
            HintAr = "تبديل مظهر التطبيق بين الفاتح والداكن",
            Value = SettingsValueKind.Bool,
            DefaultLiteral = "false",
            AutomationId = "Settings_DarkModeToggle",
            AutomationIdMobile = "Settings_Mobile_DarkModeToggle",
        },
        new("close_behavior_action", SettingsShape.EnumPicker, SettingsScope.Global,
            "عند الضغط على X", "general", 50)
        {
            HintAr = "سلوك التطبيق عند إغلاق النافذة",
            Options =
            [
                new("0", "سؤالني كل مرة"),
                new("1", "إغلاق نهائي"),
                new("2", "الإغلاق إلى الدرج"),
            ],
            Value = SettingsValueKind.Enum,
            Validation = SettingsValidationKind.EnumIndex,
            ErrorAr = "اختر سلوكًا معروفًا للإغلاق.",
            DefaultLiteral = "0",
            AutomationId = "Settings_CloseBehaviorPicker",
        },
        new("close_behavior_remembered", SettingsShape.Toggle, SettingsScope.Global,
            "تذكر خياري", "general", 60)
        {
            HintAr = "لا تسألني مرة أخرى بعد اختيار السلوك",
            Value = SettingsValueKind.Bool,
            DefaultLiteral = "false",
            Visible = false, // owned by the close-confirmation dialog, not the page
        },

        // ── Section: actions ─────────────────────────────────────────────────────────────
        new("save", SettingsShape.ActionButton, SettingsScope.Global,
            "حفظ التغييرات", "actions", 10)
        {
            CommandName = "save",
            AutomationId = "Settings_SaveButton",
        },
    ];
}
