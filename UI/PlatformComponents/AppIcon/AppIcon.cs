using Microsoft.Maui.Controls;

namespace MostaqlK.UI.PlatformComponents;

/// <summary>
/// Shared icon unit, rendered as a raster <see cref="Image"/> whose source is chosen from the
/// <see cref="Icon"/> bindable property (see <see cref="AppIconGlyph"/> for the covered icons).
///
/// NOTE: this unit originally rendered icons via a bundled FontAwesome icon *font* (a
/// <see cref="Label"/> + custom glyph codepoints). That approach was abandoned after extensive
/// investigation confirmed a genuine, unresolvable platform limitation: on this app's
/// unpackaged Windows build, WinUI's native text stack never loads runtime-referenced custom
/// font files for a <c>Microsoft.UI.Xaml.Media.FontFamily</c> (tried: MAUI's own
/// <c>fonts.AddFont</c> alias, the documented raw "&lt;file&gt;.ttf#&lt;family&gt;" descriptor
/// with both relative and absolute paths, and a <c>LabelHandler</c> mapper bypassing MAUI's
/// font-resolution pipeline entirely — all confirmed via debug logging to execute correctly,
/// yet the glyph still rendered as an empty "tofu" box). A standalone browser test confirmed
/// the actual <c>.ttf</c> files themselves are valid and render correctly, isolating the issue
/// to WinUI's native font loader specifically. Switched instead to real FontAwesome SVG icons,
/// pre-rasterized to PNG at build time by MAUI's existing <c>MauiImage</c> pipeline
/// (<c>Resources/Images/icon_*.svg</c>) — a fundamentally different, more reliable rendering
/// path that does not depend on runtime font loading at all.
/// </summary>
public partial class AppIcon : ContentView
{
    private readonly Image _image = new() { Aspect = Aspect.AspectFit };

    public static readonly BindableProperty IconProperty =
        BindableProperty.Create(nameof(Icon), typeof(AppIconGlyph), typeof(AppIcon), AppIconGlyph.Info,
            propertyChanged: OnIconChanged);

    public static readonly BindableProperty FontSizeProperty =
        BindableProperty.Create(nameof(FontSize), typeof(double), typeof(AppIcon), 16.0,
            propertyChanged: OnFontSizeChanged);

    public static readonly BindableProperty TextColorProperty =
        BindableProperty.Create(nameof(TextColor), typeof(Color), typeof(AppIcon), null,
            propertyChanged: OnTextColorChanged);

    public AppIconGlyph Icon
    {
        get => (AppIconGlyph)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    /// <summary>Kept for XAML/API compatibility with the previous font-based unit; drives the icon's pixel size (width/height), not an actual font size.</summary>
    public double FontSize
    {
        get => (double)GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    /// <summary>
    /// Kept for XAML/API compatibility with the previous font-based unit. Since the icon is now
    /// a pre-rasterized PNG (not a live-tintable glyph), only the two design-system colors this
    /// app actually needs — active blue (<c>#2563EB</c>) and inactive gray (<c>#475569</c>) —
    /// are supported by swapping to a pre-baked colored variant of the source image; any other
    /// color value is ignored (falls back to the default/inactive variant).
    /// </summary>
    public Color? TextColor
    {
        get => (Color?)GetValue(TextColorProperty);
        set => SetValue(TextColorProperty, value);
    }

    public AppIcon()
    {
        Content = _image;
        ApplySize(FontSize);
        ApplyGlyph(Icon, TextColor);
    }

    private static void OnIconChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var self = (AppIcon)bindable;
        self.ApplyGlyph((AppIconGlyph)newValue, self.TextColor);
    }

    private static void OnFontSizeChanged(BindableObject bindable, object oldValue, object newValue)
    {
        ((AppIcon)bindable).ApplySize((double)newValue);
    }

    private static void OnTextColorChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var self = (AppIcon)bindable;
        self.ApplyGlyph(self.Icon, (Color?)newValue);
    }

    private void ApplySize(double size)
    {
        _image.WidthRequest = size;
        _image.HeightRequest = size;
    }

    private void ApplyGlyph(AppIconGlyph icon, Color? textColor)
    {
        _image.Source = icon.ToImageSource(textColor);
    }
}
