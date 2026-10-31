using System.Collections.Generic;
using System.Linq;

namespace MostaqlK.UI.DesignSystem.Tokens;

/// <summary>
/// The complete theme-aware color table (single authority). Every pair is copied verbatim from
/// the pre-token rendering, so migrating a consumer to a token cannot change its pixels; aliases
/// are intentional where the same pair carries a different role (TextOnAccent vs Surface).
/// <see cref="ThemeTokens"/> writes these into the application root dictionary at startup and on
/// every theme flip.
/// </summary>
public static class NeutralTokenSet
{
    public static readonly IReadOnlyList<SemanticToken> All = new SemanticToken[]
    {
        // Neutrals / text (layout screens).
        new("TextMuted", "#64748B", "#94A3B8"),
        new("TextPrimary", "#0F172A", "#F1F5F9"),
        new("TextStrong", "#0F172A", "#E2E8F0"),
        new("TextSecondary", "#475569", "#94A3B8"),
        new("TextSecondaryStrong", "#475569", "#CBD5E1"),
        new("TextFaint", "#94A3B8", "#64748B"),
        new("TextSubtle", "#94A3B8", "#94A3B8"),
        new("TextDescription", "#64748B", "#C9D3E6"),
        new("TextDisplay", "#172033", "#FFFFFF"),
        new("TextInverse", "#F1F5F9", "#0F172A"),
        new("TextOnAccent", "#FFFFFF", "#0F172A"),

        // Surfaces.
        new("Background", "#F1F5F9", "#020617"),
        new("BackgroundChrome", "#F1F5F9", "#0F172A"),
        new("Surface", "#FFFFFF", "#0F172A"),
        new("SurfaceRaised", "#FFFFFF", "#1E293B"),
        new("SurfaceSunken", "#F1F5F9", "#1E293B"),
        new("SurfaceHover", "#F8FAFC", "#172032"),
        new("SurfaceField", "#F8FAFC", "#1E293B"),
        new("SurfaceFieldAlt", "#F8FAFC", "#18233B"),
        new("SurfaceMuted", "#F8FAFC", "#0F172A"),
        new("SurfaceDeep", "#FFFFFF", "#0B1424"),
        new("SurfaceShellAlt", "#EEF1F6", "#0B1424"),
        new("SurfaceTooltip", "#FFFFFF", "#0B1220"),
        new("SurfacePositiveSoft", "#EAFBF1", "#2A3655"),
        new("SurfaceWarningSoft", "#FBF3E3", "#18233B"),
        new("SurfaceDangerSoft", "#FEF2F2", "#2A1414"),
        new("PositiveWash", "#262E9E6B", "#262E9E6B"),

        // Borders.
        new("Border", "#E2E8F0", "#334155"),
        new("BorderSubtle", "#E2E8F0", "#1E293B"),
        new("BorderMuted", "#CBD5E1", "#475569"),
        new("BorderInverse", "#334155", "#E2E8F0"),
        new("BorderPositive", "#BBF0CE", "#2E3B5C"),
        new("BorderDanger", "#FECACA", "#3F1D1D"),

        // Accents and state.
        new("Accent", "#2386C8", "#5CA8DE"),
        new("AccentSoft", "#EFF6FF", "#132237"),
        new("AccentSoftRaised", "#EFF6FF", "#132239"),
        new("AccentSoftDeep", "#EFF6FF", "#0B2540"),
        new("AccentSoftPanel", "#EFF6FF", "#1A2A44"),
        new("AccentSoftStroke", "#DBEAFE", "#1E3A5F"),
        new("Positive", "#2E9E6B", "#4FBF8C"),
        new("PositiveAction", "#168F42", "#FFFFFF"),
        new("PositiveHover", "#16A34A", "#4ADE80"),
        new("Danger", "#DC2626", "#F87171"),
        new("DangerStrong", "#B91C1C", "#F87171"),
        new("Warning", "#B45309", "#FBBF24"),
        new("PollActive", "#EF4444", "#EF4444"),
        new("PollInactive", "#22C55E", "#22C55E"),

        // Data visualisation (dashboard / radar).
        new("DataQueue", "#B45309", "#F59E0B"),
        new("DataWorker", "#15803D", "#22C55E"),
        new("DataCompleted", "#0369A1", "#38BDF8"),
        new("DataError", "#B91C1C", "#EF4444"),
        new("DataIdle", "#64748B", "#7C8CA5"),
        new("DataDiscovery", "#1D6FA5", "#1D6FA5"),
        new("FixedQueueFill", "#F59E0B", "#F59E0B"),
        new("FixedDotIdle", "#334155", "#334155"),
        new("AvatarTile", "#3B82F6", "#3B82F6"),
        new("BorderField", "#D7E0EE", "#D7E0EE"),
        new("PositiveAccent", "#22C55E", "#22C55E"),
        new("FixedPositiveAction", "#168F42", "#168F42"),
        new("ShadowDeep", "#020617", "#020617"),
        new("Letterbox", "#0F1B2D", "#0F1B2D"),
        new("Scrim", "#8C020617", "#8C020617"),
        new("ShimmerBase", "#E0E0E0", "#E0E0E0"),
        new("ShimmerHighlight", "#40FFFFFF", "#40FFFFFF"),
        new("ShimmerClear", "#00FFFFFF", "#00FFFFFF"),
        new("ShimmerSoft", "#33FFFFFF", "#33FFFFFF"),
        new("OverlayLight", "#15FFFFFF", "#15FFFFFF"),
        new("OverlayDark", "#0A000000", "#0A000000"),
        new("RadarSweepLight", "#334155", "#334155"),
        new("OnboardingDotInactive", "#D7E0EE", "#4B5A78"),
        new("OnboardingDotActive", "#FFFFFF", "#FFFFFF"),
        new("BadgeEnrichedBackground", "#ECFDF5", "#ECFDF5"),
        new("BadgePendingBackground", "#FFFBEB", "#FFFBEB"),
        new("BadgeFailedBackground", "#FEF2F2", "#FEF2F2"),
        new("BadgeEnrichedForeground", "#2E9E6B", "#2E9E6B"),
        new("BadgePendingForeground", "#D97706", "#D97706"),
        new("BadgeFailedForeground", "#DC2626", "#DC2626"),

        // MAUI control chrome (template palette, preserved values).
        new("ControlText", "#000000", "#FFFFFF"),
        new("ControlTextStrong", "#212121", "#FFFFFF"),
        new("ControlTextSoft", "#ACACAC", "#404040"),
        new("ControlTextDisabled", "#919191", "#404040"),
        new("ControlAccentText", "#FFFFFF", "#242424"),
        new("ControlAccentSolid", "#512BD4", "#FFFFFF"),
        new("ControlAccent", "#512BD4", "#AC99EA"),
        new("ControlInkAlt", "#212121", "#C8C8C8"),
        new("ControlPlaceholder", "#C8C8C8", "#6E6E6E"),
        new("ControlFillDisabled", "#C8C8C8", "#404040"),
        new("ControlTrackActive", "#141414", "#E1E1E1"),
        new("ControlChromeDeep", "#141414", "#C8C8C8"),
        new("ControlThumbOff", "#919191", "#6E6E6E"),
        new("ControlToggleOnSoft", "#DFD8F7", "#C8C8C8"),
        new("ControlSearchChrome", "#6E6E6E", "#6E6E6E"),
        new("SwitchThumb", "#FFFFFF", "#FFFFFF"),
        new("ShadowWhite", "#FFFFFF", "#FFFFFF"),
        new("HeadlineInk", "#190649", "#FFFFFF"),

        // Shell / navigation chrome.
        new("ShellBackground", "#FFFFFF", "#1F1F1F"),
        new("ShellInk", "#000000", "#9880E5"),
        new("ShellDisabled", "#C8C8C8", "#141414"),
        new("ShellUnselected", "#C8C8C8", "#C8C8C8"),
        new("ShellTabBar", "#FFFFFF", "#000000"),
        new("ShellTabAccent", "#D600AA", "#FFFFFF"),
        new("NavigationBarInk", "#C8C8C8", "#FFFFFF"),
        new("TabBarBackground", "#FFFFFF", "#141414"),

        // Native title bar (Windows chrome reads these as Colors, never DynamicResource).
        new("TitleBarBackground", "#FFFFFF", "#1E1E1E"),
        new("TitleBarInk", "#000000", "#FFFFFF"),
        new("TitleBarHover", "#E6E6E6", "#3C3C3C"),
        new("TitleBarPressed", "#D2D2D2", "#505050"),

        // Theme-invariant values migrated from the retired primitive palette. They keep the
        // pre-token rendering exactly (a 1-to-1 replacement of a StaticResource key that never
        // varied by theme); re-pointing them at semantic theme-aware roles is a separate decision.
        new("FixedWhite", "#FFFFFF", "#FFFFFF"),
        new("FixedBlack", "#000000", "#000000"),
        new("FixedGray", "#808080", "#808080"),
        new("FixedInkMuted", "#64748B", "#64748B"),
        new("FixedGray100", "#E1E1E1", "#E1E1E1"),
        new("FixedGray200", "#C8C8C8", "#C8C8C8"),
        new("FixedGray300", "#ACACAC", "#ACACAC"),
        new("FixedGray400", "#919191", "#919191"),
        new("FixedGray500", "#6E6E6E", "#6E6E6E"),
        new("FixedGray600", "#404040", "#404040"),
        new("FixedGray900", "#212121", "#212121"),
        new("FixedGray950", "#141414", "#141414"),
        new("FixedPurple", "#512BD4", "#512BD4"),
        new("FixedPurpleDark", "#AC99EA", "#AC99EA"),
        new("FixedPurpleText", "#242424", "#242424"),
        new("FixedSecondary", "#DFD8F7", "#DFD8F7"),
        new("FixedSecondaryText", "#9880E5", "#9880E5"),
        new("FixedTertiary", "#2B0B98", "#2B0B98"),
        new("FixedMagenta", "#D600AA", "#D600AA"),
        new("FixedMidnight", "#190649", "#190649"),
        new("FixedOffBlack", "#1F1F1F", "#1F1F1F"),
        new("FixedAccent", "#2386C8", "#2386C8"),
        new("FixedAccentDark", "#5CA8DE", "#5CA8DE"),
        new("FixedPositive", "#2E9E6B", "#2E9E6B"),
        new("FixedPositiveDark", "#4FBF8C", "#4FBF8C"),
        new("FixedBackgroundLight", "#F1F5F9", "#F1F5F9"),
        new("FixedBackgroundDark", "#0F172A", "#0F172A"),
        new("FixedSurfaceLight", "#FFFFFF", "#FFFFFF"),
        new("FixedSurfaceDark", "#0F172A", "#0F172A"),
        new("FixedReadBorderLight", "#CBD5E1", "#CBD5E1"),
        new("FixedReadBorderDark", "#475569", "#475569"),
    };

    /// <summary>Tokens consumed by Brush-typed properties (Stroke / Shadow.Brush); the bridge
    /// writes a SolidColorBrush twin named <c>{Token}Brush</c> for each.</summary>
    public static readonly IReadOnlySet<string> BrushTokens = new HashSet<string>(StringComparer.Ordinal)
    {
        "Border",
        "BorderSubtle",
        "BorderMuted",
        "ControlPlaceholder",
        "BorderInverse",
        "BorderPositive",
        "BorderDanger",
        "Accent",
        "AccentSoftStroke",
        "ShadowWhite",
        "BackgroundChrome",
        "Surface",
        "SurfaceField",
        "Positive",
        "BorderField",
        "ShadowDeep",
        "FixedWhite",
        "FixedBlack",
        "FixedGray100",
        "FixedGray200",
        "FixedGray300",
        "FixedGray400",
        "FixedGray500",
        "FixedGray600",
        "FixedGray900",
        "FixedGray950",
        "FixedPurple",
        "FixedSecondary",
        "FixedTertiary",
        "FixedAccent",
        "FixedAccentDark",
        "FixedPositive",
        "FixedPositiveDark",
        "FixedBackgroundLight",
        "FixedBackgroundDark",
        "FixedSurfaceLight",
        "FixedSurfaceDark",
        "FixedReadBorderLight",
        "FixedReadBorderDark",
    };

    private static readonly Dictionary<string, SemanticToken> ByName =
        All.ToDictionary(static token => token.Name, StringComparer.Ordinal);

    public static SemanticToken? Find(string name) =>
        ByName.TryGetValue(name, out var token) ? token : null;
}
