namespace MostaqlK.UI.PlatformComponents.SourceRail;

/// <summary>
/// The single source of truth for every rail-width pixel in the v3 shell (contract §3): the
/// <see cref="SourceRail"/> unit sizes itself from these constants, and
/// <c>MainWindowWindowsLayout</c>'s splitter clamp reads <see cref="Width"/> instead of the old
/// hardcoded 256px sidebar constant. A future rail redesign changes ONE number here, never four
/// call sites. Values come straight from the approved v3 mockups' <c>.rail</c> CSS
/// (64px wide rail, 44px icon buttons, 40px brand mark, 3×22px active indicator bar).
/// </summary>
public static class RailWidths
{
    /// <summary>Total rail width (<c>.rail { width: 64px }</c>).</summary>
    public const double Width = 64;

    /// <summary>One rail icon button / source tile (<c>.rail-btn { width: 44px; height: 44px }</c>).</summary>
    public const double TileSize = 44;

    /// <summary>Brand mark tile (<c>.rail .w-10 .h-10</c> = 40px).</summary>
    public const double BrandMarkSize = 40;

    /// <summary>Active indicator bar geometry (<c>.rail-btn.active::before</c>: 3px wide, 22px tall).</summary>
    public const double IndicatorWidth = 3;

    public const double IndicatorHeight = 22;
}
