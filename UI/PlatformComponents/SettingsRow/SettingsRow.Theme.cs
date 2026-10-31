using Microsoft.Maui;
using MostaqlK.Core.Sources;
using MostaqlK.UI.DesignSystem.BrandTokens;
using MostaqlK.UI.DesignSystem.Tokens;

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
    private void OnRowLoaded(object? sender, EventArgs e)
    {
        ThemeTokens.TokensChanged -= OnPaletteTokensChanged;
        ThemeTokens.TokensChanged += OnPaletteTokensChanged;
    }

    private void OnRowUnloaded(object? sender, EventArgs e) =>
        ThemeTokens.TokensChanged -= OnPaletteTokensChanged;

    private void OnPaletteTokensChanged() => ApplyTheme();

    private void ApplyTheme()
    {
        _titleLabel.TextColor = ThemeTokens.TextSecondaryStrong;
        _hintLabel.TextColor = _row?.Descriptor.HintTone switch
        {
            SettingsHintTone.Brand => BrandPalette.BrandInk,
            SettingsHintTone.Success => ThemeTokens.Positive,
            _ => ThemeTokens.TextMuted,
        };
        _errorLabel.TextColor = ThemeTokens.Danger;
        _divider.Color = ThemeTokens.Border;

        foreach (var child in _valueCell.Children)
        {
            PaintValue(child);
            if (child is Border box && box.Content is IView inner)
            {
                box.Stroke = _row?.HasError == true ? ThemeTokens.Danger : ThemeTokens.Border;
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
                label.TextColor = ThemeTokens.TextPrimary;
                break;
            case Label small:
                small.TextColor = ThemeTokens.TextMuted;
                break;
            case Entry entry:
                entry.TextColor = ThemeTokens.TextPrimary;
                entry.PlaceholderColor = ThemeTokens.TextMuted;
                entry.BackgroundColor = Colors.Transparent;
                break;
            case Picker picker:
                picker.TextColor = ThemeTokens.TextPrimary;
                break;
            case Slider slider:
                slider.MinimumTrackColor = BrandPalette.Brand;
                slider.MaximumTrackColor = ThemeTokens.Border;
                slider.ThumbColor = BrandPalette.Brand;
                break;
        }
    }

    private void PaintChip(Button chip, bool pressed)
    {
        chip.BackgroundColor = pressed ? BrandPalette.BrandSoft : ThemeTokens.SurfaceRaised;
        chip.TextColor = pressed ? BrandPalette.BrandInk : ThemeTokens.TextSecondaryStrong;
        chip.BorderColor = pressed ? Colors.Transparent : ThemeTokens.Border;
    }
}
