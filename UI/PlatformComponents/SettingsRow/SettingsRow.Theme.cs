using Microsoft.Maui;
using MostaqlK.Core.Sources;
using MostaqlK.UI.DesignSystem.BrandTokens;

namespace MostaqlK.UI.PlatformComponents.SettingsRow;

/// <summary>
/// Theme application for the row: brand colors come live from <see cref="BrandPalette"/> (they
/// follow a source morph), and the neutral ink/line/surface/danger values mirror the exact hexes of
/// the v3 settings mockup tokens (<c>--ink-1/2/3</c>, <c>--line</c>, <c>--surface(-2)</c>,
/// <c>--danger</c>, <c>--accent-positive</c>) so the renderer and the design never drift.
/// Re-applied on theme change and on source swap.
/// </summary>
public partial class SettingsRow
{
    private static readonly Color Ink1Light = Color.FromArgb("#0F172A");
    private static readonly Color Ink2Light = Color.FromArgb("#475569");
    private static readonly Color Ink3Light = Color.FromArgb("#64748B");
    private static readonly Color LineLight = Color.FromArgb("#E2E8F0");
    private static readonly Color DangerLight = Color.FromArgb("#DC2626");
    private static readonly Color PositiveLight = Color.FromArgb("#2E9E6B");
    private static readonly Color SurfaceLight = Color.FromArgb("#FFFFFF");
    private static readonly Color Surface2Light = Color.FromArgb("#F8FAFC");

    private static readonly Color Ink1Dark = Color.FromArgb("#F1F5F9");
    private static readonly Color Ink2Dark = Color.FromArgb("#CBD5E1");
    private static readonly Color Ink3Dark = Color.FromArgb("#94A3B8");
    private static readonly Color LineDark = Color.FromArgb("#334155");
    private static readonly Color DangerDark = Color.FromArgb("#F87171");
    private static readonly Color PositiveDark = Color.FromArgb("#4FBF8C");
    private static readonly Color SurfaceDark = Color.FromArgb("#1E293B");
    private static readonly Color Surface2Dark = Color.FromArgb("#172032");

    private static bool IsDark => Application.Current?.RequestedTheme == AppTheme.Dark;

    private static Color Ink1 => IsDark ? Ink1Dark : Ink1Light;
    private static Color Ink2 => IsDark ? Ink2Dark : Ink2Light;
    private static Color Ink3 => IsDark ? Ink3Dark : Ink3Light;
    private static Color Line => IsDark ? LineDark : LineLight;
    private static Color Danger => IsDark ? DangerDark : DangerLight;

    private void ApplyTheme()
    {
        _titleLabel.TextColor = Ink2;
        _hintLabel.TextColor = _row?.Descriptor.HintTone switch
        {
            SettingsHintTone.Brand => BrandPalette.BrandInk,
            SettingsHintTone.Success => IsDark ? PositiveDark : PositiveLight,
            _ => Ink3,
        };
        _errorLabel.TextColor = Danger;
        _divider.Color = Line;

        foreach (var child in _valueCell.Children)
        {
            PaintValue(child);
            if (child is Border box && box.Content is IView inner)
            {
                box.Stroke = _row?.HasError == true ? Danger : Line;
                PaintValue(inner);
            }
        }
    }

    /// <summary>
    /// Paints the value cell's text and fields. Buttons are deliberately NOT touched here: each
    /// button builder (<c>PrimaryButton</c> / <c>GhostButton</c> / <c>LinkAction</c>) already owns
    /// its mockup's exact treatment, and repainting by shape would flatten the three apart.
    /// </summary>
    private static void PaintValue(IView view)
    {
        switch (view)
        {
            case Label label when label.FontSize >= LabelSize:
                label.TextColor = Ink1;
                break;
            case Label small:
                small.TextColor = Ink3;
                break;
            case Entry entry:
                entry.TextColor = Ink1;
                entry.PlaceholderColor = Ink3;
                entry.BackgroundColor = Colors.Transparent;
                break;
            case Picker picker:
                picker.TextColor = Ink1;
                break;
            case Slider slider:
                slider.MinimumTrackColor = BrandPalette.Brand;
                slider.MaximumTrackColor = Line;
                slider.ThumbColor = BrandPalette.Brand;
                break;
        }
    }

    private void PaintChip(Button chip, bool pressed)
    {
        chip.BackgroundColor = pressed ? BrandPalette.BrandSoft : IsDark ? SurfaceDark : SurfaceLight;
        chip.TextColor = pressed ? BrandPalette.BrandInk : Ink2;
        chip.BorderColor = pressed ? Colors.Transparent : Line;
    }
}
