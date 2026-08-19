using Microsoft.Maui.Graphics;

namespace MostaqlK.UI.DesignSystem;

/// <summary>
/// Central source of truth for brand colors, spacing, and corner-radius tokens used across the
/// MostaqlK design system. Values are derived from the MVP mockups
/// (.repertoire/design/mvp/*.html) and mirror the resources declared in
/// Resources/Styles/Colors.xaml. Keep the two in sync when either changes.
/// </summary>
public static class DesignTokens
{
    /// <summary>Brand and semantic colors (light/dark pairs).</summary>
    public static class Colors
    {
        // Mostaql blue accent.
        public static readonly Color AccentPrimary = Color.FromArgb("#2386C8");
        public static readonly Color AccentPrimaryDark = Color.FromArgb("#5CA8DE");

        // Positive/success accent.
        public static readonly Color AccentPositive = Color.FromArgb("#2E9E6B");
        public static readonly Color AccentPositiveDark = Color.FromArgb("#4FBF8C");

        // Polling toggle (running vs stopped) semantic colors.
        // Mirrors the MVP mockup (projects.html) and the existing pipeline error/success dots.
        public static readonly Color PollToggleActive = Color.FromArgb("#EF4444");
        public static readonly Color PollToggleInactive = Color.FromArgb("#22C55E");

        // Slate background/surface palette.
        public static readonly Color BackgroundLight = Color.FromArgb("#F8FAFC");
        public static readonly Color BackgroundDark = Color.FromArgb("#0F172A");
        public static readonly Color SurfaceLight = Color.FromArgb("#FFFFFF");
        public static readonly Color SurfaceDark = Color.FromArgb("#1E293B");

        // Muted "read" project-card border/accent colors (unread accent bar uses AccentPrimary instead).
        public static readonly Color ReadBorderLight = Color.FromArgb("#CBD5E1");
        public static readonly Color ReadBorderDark = Color.FromArgb("#475569");
    }

    /// <summary>Spacing scale, in device-independent units. No ad-hoc numeric literals per system-components.md.</summary>
    public static class Spacing
    {
        public const double XS = 4d;
        public const double S = 8d;
        public const double M = 12d;
        public const double L = 16d;
        public const double XL = 24d;
    }

    /// <summary>Corner-radius tokens (rounded-xl style used across cards/buttons/inputs).</summary>
    public static class CornerRadius
    {
        public const double Small = 6d;
        public const double Default = 12d; // "rounded-xl" equivalent
        public const double Large = 16d;
    }
}
