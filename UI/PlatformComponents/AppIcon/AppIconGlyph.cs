namespace MostaqlK.UI.PlatformComponents;

/// <summary>
/// Every icon actually used across the 4 design mockups (projects.html, project-details.html,
/// settings.html, about.html), named after its semantic use in the app rather than the raw
/// FontAwesome icon name it originated from (kept in each doc comment below for traceability).
/// Only some values have real rasterized artwork under <c>Resources/Images/</c> — see
/// <see cref="AppIconGlyphExtensions"/> for the resolution mechanism; the rest fall back to the
/// "info" icon until dedicated artwork is added.
/// </summary>
public enum AppIconGlyph
{
    /// <summary>Sidebar "المشاريع" nav item. Solid <c>fa-list-check</c> (f0ae).</summary>
    ProjectsList,

    /// <summary>Sidebar "البحث المتقدم" nav item + search-box icon. Solid <c>fa-magnifying-glass</c> (f002).</summary>
    Search,

    /// <summary>Sidebar "التنبيهات" nav item. Regular <c>fa-bell</c> (f0f3).</summary>
    Bell,

    /// <summary>Sidebar "الإعدادات" nav item + settings-page header/toolbar icon. Solid <c>fa-gear</c> (f013).</summary>
    Gear,

    /// <summary>Sidebar "حول التطبيق" nav item + about-page header/MVP-note icon. Solid <c>fa-circle-info</c> (f05a).</summary>
    Info,

    /// <summary>Dark-mode row moon icon. Regular <c>fa-moon</c> (f186).</summary>
    Moon,

    /// <summary>Poll-toggle "إيقاف" button icon. Solid <c>fa-pause</c> (f04c).</summary>
    Pause,

    /// <summary>Poll-toggle "تشغيل" button icon. Solid <c>fa-play</c> (f04b).</summary>
    Play,

    /// <summary>Status-bar request-rate icon. Solid <c>fa-gauge-high</c> (f625).</summary>
    Gauge,

    /// <summary>About page "حقائق سريعة" section header icon. Solid <c>fa-list</c> (f03a).</summary>
    List,

    /// <summary>About page storage fact-row icon. Solid <c>fa-database</c> (f1c0).</summary>
    Database,

    /// <summary>About page data-policy fact-row icon. Solid <c>fa-box-archive</c> (f187).</summary>
    Archive,

    /// <summary>About page language fact-row icon. Solid <c>fa-language</c> (f1ab).</summary>
    Language,

    /// <summary>Project-details "العودة للمشاريع" back-link icon. Solid <c>fa-arrow-right</c> (f061).</summary>
    ArrowRight,

    /// <summary>Project-details "تم الإثراء" enriched-status badge icon. Regular <c>fa-circle-check</c> (f058).</summary>
    CircleCheck,

    /// <summary>Project-details "نُشر منذ ..." published-time icon. Regular <c>fa-clock</c> (f017).</summary>
    Clock,

    /// <summary>Project-details "تفاصيل المشروع" section header icon. Solid <c>fa-file-lines</c> (f15c).</summary>
    FileLines,

    /// <summary>Project-details "المهارات المطلوبة" section header icon. Solid <c>fa-code</c> (f121).</summary>
    Code,

    /// <summary>Project-details "المرفقات" section header icon. Solid <c>fa-paperclip</c> (f0c6).</summary>
    Paperclip,

    /// <summary>Project-details attachment image-thumbnail placeholder icon. Regular <c>fa-image</c> (f03e).</summary>
    Image,

    /// <summary>
    /// Project-details attachment download icon. The mockup's <c>fa-arrow-down-to-line</c> class
    /// does not exist in the FontAwesome 6 Free set (verified against the official CSS — only the
    /// Pro-only <c>fa-tent-arrow-down-to-line</c> variant exists), so this resolves to the closest
    /// free equivalent, solid <c>fa-download</c> (f019).
    /// </summary>
    Download,

    /// <summary>Settings page "استعلام الفحص" section header icon, and the projects.html active-query bar icon. Solid <c>fa-filter</c> (f0b0).</summary>
    Filter,

    /// <summary>Projects feed "عدد المقترحات" stat icon. Solid <c>fa-users</c> (f0c0).</summary>
    Users,

    /// <summary>Settings page query-params-preview icon. Solid <c>fa-link</c> (f0c1).</summary>
    Link,

    /// <summary>Settings page "؟" help hint icon. Regular <c>fa-circle-question</c> (f059).</summary>
    CircleQuestion,

    /// <summary>Settings page "الفاصل الزمني للفحص" card icon. Solid <c>fa-stopwatch</c> (f2f2).</summary>
    Stopwatch,

    /// <summary>Settings page "تجميع التنبيهات" card icon. Solid <c>fa-layer-group</c> (f5fd).</summary>
    LayerGroup,

    /// <summary>Settings page cookie upload icon. Solid <c>fa-upload</c> (f093).</summary>
    Upload,

    /// <summary>Project card edit icon. Solid <c>fa-pen-to-square</c> (f044).</summary>
    Edit,

    /// <summary>Footer refresh icon. Solid <c>fa-rotate-right</c> (f01e).</summary>
    Refresh,

    /// <summary>Chevron right icon. Solid <c>fa-chevron-right</c> (f054).</summary>
    ChevronRight,

    /// <summary>Chevron left icon. Solid <c>fa-chevron-left</c> (f053).</summary>
    ChevronLeft,

    /// <summary>Close icon. Solid <c>fa-xmark</c> (f00d).</summary>
    Close,

    /// <summary>About page "platform" fact-row icon. Brands <c>fa-windows</c> (f17a).</summary>
    Windows,
    
    /// <summary>Onboarding badge lightning icon. Solid <c>fa-bolt</c> (f0e7).</summary>
    Bolt,
}
