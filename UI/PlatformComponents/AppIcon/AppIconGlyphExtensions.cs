using System;
using System.IO;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace MostaqlK.UI.PlatformComponents;

/// <summary>
/// Resolves each <see cref="AppIconGlyph"/> to a pre-rasterized <c>Resources/Images/icon_*.png</c>
/// image (see <see cref="AppIcon"/>'s class doc for the rendering mechanism and why it replaced
/// the original FontAwesome font-glyph approach).
/// </summary>
public static class AppIconGlyphExtensions
{
    private const string ActiveTextHex = "#2563EB"; // matches AppSidebar.ActiveText

    /// <summary>Owner "verified" badge green in projects.html (<c>text-green-500</c>).</summary>
    private const string VerifiedGreenHex = "#22C55E";

    /// <summary>"قيد الإثراء" badge amber in projects.html (<c>text-amber-600</c>).</summary>
    private const string PendingAmberHex = "#D97706";

    /// <summary>"فشل الإثراء" badge red — no mockup counterpart, matches the app's own badge colour.</summary>
    private const string FailedRedHex = "#DC2626";

    // Tier 3 — settings-row conceptual colors, per DESIGN.md § icon system
    private const string SettingPollIndigoHex = "#6366F1";
    private const string SettingQueryVioletHex = "#8B5CF6";
    private const string SettingAssetsOrangeHex = "#F97316";
    private const string SettingGroupingTealHex = "#14B8A6";
    private const string SettingRatePinkHex = "#EC4899";

    /// <summary>
    /// Maps an icon to its base (inactive/default) image resource name, and — for the 5
    /// sidebar nav icons that need an active-state color swap — an "_active" (<c>#2563EB</c>)
    /// blue variant baked as a separate pre-colored SVG (see <see cref="ToImageSource"/>).
    /// Icons with no dedicated SVG yet fall back to <see cref="AppIconGlyph.Info"/>'s image
    /// (documented gap: only the icons actually rendered by <c>AppSidebar</c> and the projects
    /// feed today have real artwork; the rest of the enum is reserved for future pages).
    /// </summary>
    private static string ToImageBaseName(this AppIconGlyph icon) => icon switch
    {
        AppIconGlyph.ProjectsList => "icon_list_check",
        AppIconGlyph.Search => "icon_magnifying_glass",
        AppIconGlyph.Bell => "icon_bell",
        AppIconGlyph.Gear => "icon_gear",
        AppIconGlyph.Info => "icon_circle_info",
        AppIconGlyph.Moon => "icon_moon",
        AppIconGlyph.Filter => "icon_filter",
        AppIconGlyph.Pause => "icon_pause",
        AppIconGlyph.Play => "icon_play",
        AppIconGlyph.Users => "icon_users",
        AppIconGlyph.CircleCheck => "icon_circle_check",
        AppIconGlyph.Clock => "icon_clock",
        AppIconGlyph.Stopwatch => "icon_stopwatch",
        AppIconGlyph.LayerGroup => "icon_layer_group",
        AppIconGlyph.Paperclip => "icon_paperclip",
        AppIconGlyph.Gauge => "icon_gauge_high",
        AppIconGlyph.CircleQuestion => "icon_circle_question",
        AppIconGlyph.Upload => "icon_upload",
        AppIconGlyph.Link => "icon_link",
        AppIconGlyph.Edit => "icon_edit",
        AppIconGlyph.Refresh => "icon_refresh",
        AppIconGlyph.ChevronRight => "icon_chevron_right",
        AppIconGlyph.ChevronLeft => "icon_chevron_left",
        AppIconGlyph.Close => "icon_close",
        AppIconGlyph.Windows => "icon_windows",
        AppIconGlyph.Database => "icon_database",
        AppIconGlyph.Archive => "icon_box_archive",
        AppIconGlyph.Language => "icon_language",
        AppIconGlyph.List => "icon_list",
        AppIconGlyph.Bolt => "icon_bolt",
        _ => "icon_circle_info",
    };

    /// <summary>
    /// The nav icons that ship an "_active" blue variant. Only these may take the active swap —
    /// asking for e.g. <c>icon_filter_active</c> would resolve to a file that does not exist and
    /// render nothing at all.
    /// </summary>
    private static readonly string[] ActiveVariantIcons =
    [
        "icon_list_check",
        "icon_magnifying_glass",
        "icon_bell",
        "icon_gear",
        "icon_circle_info",
        "icon_edit",
    ];

    /// <summary>
    /// Pre-baked colour variants beyond the nav icons' inactive/active pair. A rasterized PNG
    /// cannot be tinted at runtime (see <see cref="ToImageSource"/>), so every colour an icon is
    /// drawn in by the mockups needs its own file. Returns <c>null</c> when the requested colour
    /// has no dedicated variant, in which case the base file is used.
    /// </summary>
    private static string? ToColourVariant(string baseName, Color textColor) => (baseName, textColor) switch
    {
        // The owner row's verification badge is the *solid* circle-check in a lighter green than
        // the "تم الإثراء" badge's regular-weight one.
        ("icon_circle_check", _) when textColor.Equals(Color.FromArgb(VerifiedGreenHex)) => "_verified",

        // The "قيد الإثراء" badge reuses the clock in the badge's own amber.
        ("icon_clock", _) when textColor.Equals(Color.FromArgb(PendingAmberHex)) => "_amber",

        // The failure badge has no mockup counterpart; it reuses the clock in the badge's red.
        ("icon_clock", _) when textColor.Equals(Color.FromArgb(FailedRedHex)) => "_red",

        // Settings page conceptual colors (Tier 3)
        ("icon_stopwatch", _) when textColor.Equals(Color.FromArgb(SettingPollIndigoHex)) => "_indigo",
        ("icon_filter", _) when textColor.Equals(Color.FromArgb(SettingQueryVioletHex)) => "_violet",
        ("icon_paperclip", _) when textColor.Equals(Color.FromArgb(SettingAssetsOrangeHex)) => "_orange",
        ("icon_layer_group", _) when textColor.Equals(Color.FromArgb(SettingGroupingTealHex)) => "_teal",
        ("icon_gauge_high", _) when textColor.Equals(Color.FromArgb(SettingRatePinkHex)) => "_pink",
        (_, _) when textColor?.Equals(Colors.White) == true => "_white",

        _ => null,
    };

    /// <summary>
    /// Resolves the actual rasterized icon image to load, swapping in the pre-baked "_active"
    /// blue variant when <paramref name="textColor"/> matches the app's active-nav-item color
    /// (<c>#2563EB</c>), or one of the <see cref="ToColourVariant"/> colours; any other color
    /// (including the default inactive <c>#475569</c>, or no color at all) uses the
    /// base/inactive-colored PNG. <c>icon_moon</c> has no active variant (it's never shown in an
    /// active state), so it always uses its base file.
    ///
    /// NOTE: loads via <see cref="ImageSource.FromFile"/> against an absolute path under
    /// <see cref="AppContext.BaseDirectory"/> rather than the plain resource-name string form
    /// (e.g. <c>"icon_bell"</c> / <c>"icon_bell.svg"</c>). Both plain forms were confirmed —
    /// via isolated diagnostic builds — to silently fail to resolve on this app's unpackaged
    /// Windows build (the generated <c>icon_bell.scale-200.png</c> etc. files themselves were
    /// verified correct), the same class of resource-alias-resolution failure documented for
    /// custom fonts in <see cref="AppIcon"/>'s class doc. Direct absolute-path file loading is
    /// the one approach confirmed to actually work.
    /// </summary>
    public static ImageSource ToImageSource(this AppIconGlyph icon, Color? textColor)
    {
        var baseName = icon.ToImageBaseName();
        var isActive = Array.IndexOf(ActiveVariantIcons, baseName) >= 0
            && textColor is not null
            && textColor.Equals(Color.FromArgb(ActiveTextHex));
        var variant = isActive ? "_active" : (textColor is null ? null : ToColourVariant(baseName, textColor));
        var fileName = $"{baseName}{variant}.scale-200.png";
        var fullPath = Path.Combine(AppContext.BaseDirectory, fileName);
        return ImageSource.FromFile(fullPath);
    }
}
