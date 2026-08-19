using Microsoft.Maui.Graphics;
using MostaqlK.Models;
using MostaqlK.UI.PlatformComponents;

namespace MostaqlK.UI.DesignSystem.Badges;

/// <summary>
/// Authoritative single ground for project enrichment badge styling, colors, and iconography.
/// Derived from MVP mockups and design tokens.
/// </summary>
public static class EnrichmentBadgeStyle
{
    public const string EnrichedText = "تم الإثراء";
    public const string PendingText = "قيد الإثراء";
    public const string FailedText = "فشل الإثراء";

    public const string EnrichedBackgroundHex = "#ECFDF5";
    public const string PendingBackgroundHex = "#FFFBEB";
    public const string FailedBackgroundHex = "#FEF2F2";

    public const string EnrichedForegroundHex = "#2E9E6B";
    public const string PendingForegroundHex = "#D97706";
    public const string FailedForegroundHex = "#DC2626";

    /// <summary>Returns the localized label for the enrichment status.</summary>
    public static string GetText(EnrichmentStatus status) => status switch
    {
        EnrichmentStatus.Enriched => EnrichedText,
        EnrichmentStatus.Failed => FailedText,
        _ => PendingText,
    };

    /// <summary>Returns the background color hex string for the enrichment status badge.</summary>
    public static string GetBackgroundHex(EnrichmentStatus status) => status switch
    {
        EnrichmentStatus.Enriched => EnrichedBackgroundHex,
        EnrichmentStatus.Failed => FailedBackgroundHex,
        _ => PendingBackgroundHex,
    };

    /// <summary>Returns the foreground/text color hex string for the enrichment status badge.</summary>
    public static string GetForegroundHex(EnrichmentStatus status) => status switch
    {
        EnrichmentStatus.Enriched => EnrichedForegroundHex,
        EnrichmentStatus.Failed => FailedForegroundHex,
        _ => PendingForegroundHex,
    };

    /// <summary>Returns the parsed background <see cref="Color"/> for the enrichment status badge.</summary>
    public static Color GetBackgroundColor(EnrichmentStatus status) => Color.FromArgb(GetBackgroundHex(status));

    /// <summary>Returns the parsed foreground <see cref="Color"/> for the enrichment status badge.</summary>
    public static Color GetForegroundColor(EnrichmentStatus status) => Color.FromArgb(GetForegroundHex(status));

    /// <summary>Returns the iconography glyph for the enrichment status badge.</summary>
    public static AppIconGlyph GetIcon(EnrichmentStatus status) => status switch
    {
        EnrichmentStatus.Enriched => AppIconGlyph.CircleCheck,
        _ => AppIconGlyph.Clock,
    };
}
