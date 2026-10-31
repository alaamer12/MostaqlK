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

    // Badge colors are theme-invariant; the values live in the token table (single authority).
    private static string TokenHex(string name) =>
        Tokens.NeutralTokenSet.Find(name)?.Light
            ?? Tokens.NeutralTokenSet.Find("FixedBlack")?.Light
            ?? throw new System.InvalidOperationException($"Unknown badge token '{name}'.");

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
        EnrichmentStatus.Enriched => TokenHex("BadgeEnrichedBackground"),
        EnrichmentStatus.Failed => TokenHex("BadgeFailedBackground"),
        _ => TokenHex("BadgePendingBackground"),
    };

    /// <summary>Returns the foreground/text color hex string for the enrichment status badge.</summary>
    public static string GetForegroundHex(EnrichmentStatus status) => status switch
    {
        EnrichmentStatus.Enriched => TokenHex("BadgeEnrichedForeground"),
        EnrichmentStatus.Failed => TokenHex("BadgeFailedForeground"),
        _ => TokenHex("BadgePendingForeground"),
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
