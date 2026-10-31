namespace MostaqlK.UI.DesignSystem;

/// <summary>
/// Spacing and corner-radius tokens used across the MostaqlK design system. Colors moved to
/// <see cref="Tokens.NeutralTokenSet"/> / <see cref="Tokens.ThemeTokens"/> (one authority).
/// </summary>
public static class DesignTokens
{
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
