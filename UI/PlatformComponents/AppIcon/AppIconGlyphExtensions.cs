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

    /// <summary>
    /// Maps an icon to its base (inactive/default) image resource name, and — for the 5
    /// sidebar nav icons that need an active-state color swap — an "_active" (<c>#2563EB</c>)
    /// blue variant baked as a separate pre-colored SVG (see <see cref="ToImageSource"/>).
    /// Icons with no dedicated SVG yet fall back to <see cref="AppIconGlyph.Info"/>'s image
    /// (documented gap: only the 6 icons actually used by <c>AppSidebar</c> today have real
    /// artwork; the rest of the enum is reserved for future pages).
    /// </summary>
    private static string ToImageBaseName(this AppIconGlyph icon) => icon switch
    {
        AppIconGlyph.ProjectsList => "icon_list_check",
        AppIconGlyph.Search => "icon_magnifying_glass",
        AppIconGlyph.Bell => "icon_bell",
        AppIconGlyph.Gear => "icon_gear",
        AppIconGlyph.Info => "icon_circle_info",
        AppIconGlyph.Moon => "icon_moon",
        _ => "icon_circle_info",
    };

    /// <summary>
    /// Resolves the actual rasterized icon image to load, swapping in the pre-baked "_active"
    /// blue variant when <paramref name="textColor"/> matches the app's active-nav-item color
    /// (<c>#2563EB</c>); any other color (including the default inactive <c>#475569</c>, or no
    /// color at all) uses the base/inactive-colored PNG. <c>icon_moon</c> has no active variant
    /// (it's never shown in an active state), so it always uses its base file.
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
        var isActive = baseName != "icon_moon" && textColor is not null && textColor.Equals(Color.FromArgb(ActiveTextHex));
        var fileName = isActive ? $"{baseName}_active.scale-200.png" : $"{baseName}.scale-200.png";
        var fullPath = Path.Combine(AppContext.BaseDirectory, fileName);
        return ImageSource.FromFile(fullPath);
    }
}
