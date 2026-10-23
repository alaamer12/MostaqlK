using Microsoft.Maui.Controls.Shapes;
using Path = Microsoft.Maui.Controls.Shapes.Path;

namespace MostaqlK.UI.PlatformComponents;

/// <summary>
/// A live-tintable vector icon: FontAwesome 6.7.2 path data rendered through MAUI Shapes
/// (<see cref="Path"/>), so <see cref="TintColor"/> can be ANY color — including the dynamic
/// brand keys — and repaints instantly. This is the complement to <see cref="AppIcon"/>, whose
/// pre-rasterized PNGs only support the handful of baked color variants; units that must follow
/// the active source's palette (the v3 rail's nav icons: inactive ink-3 → hover ink-1 → active
/// brand-ink) use this instead. Static single ScaleTransform per instance (no per-frame churn —
/// animation-safety compliant).
/// </summary>
public class TintableIcon : ContentView
{
    private readonly Path _path = new();

    public static readonly BindableProperty IconProperty = BindableProperty.Create(
        nameof(Icon), typeof(AppIconGlyph), typeof(TintableIcon), AppIconGlyph.Info,
        propertyChanged: (b, _, _) => ((TintableIcon)b).ApplyGeometry());

    public static readonly BindableProperty SizeProperty = BindableProperty.Create(
        nameof(Size), typeof(double), typeof(TintableIcon), 18.0,
        propertyChanged: (b, _, _) => ((TintableIcon)b).ApplyGeometry());

    public static readonly BindableProperty TintColorProperty = BindableProperty.Create(
        nameof(TintColor), typeof(Color), typeof(TintableIcon), Colors.Transparent,
        propertyChanged: (b, _, _) => ((TintableIcon)b)._path.Fill = (Color)b.GetValue(TintColorProperty));

    /// <summary>Which glyph to draw (same vocabulary as <see cref="AppIcon"/>).</summary>
    public AppIconGlyph Icon
    {
        get => (AppIconGlyph)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    /// <summary>Rendered square size in DIPs (width and height).</summary>
    public double Size
    {
        get => (double)GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    /// <summary>Live fill — bind to a DynamicResource brand key or set per state.</summary>
    public Color TintColor
    {
        get => (Color)GetValue(TintColorProperty);
        set => SetValue(TintColorProperty, value);
    }

    public TintableIcon()
    {
        Content = _path;
        HorizontalOptions = LayoutOptions.Center;
        VerticalOptions = LayoutOptions.Center;
        ApplyGeometry();
    }

    private void ApplyGeometry()
    {
        if (!GlyphData.TryGetValue(Icon, out var glyph))
        {
            _path.Data = null;
            return;
        }

        var scale = Size / glyph.ViewBox;
        var figures = new PathFigureCollection();
        PathFigureCollectionConverter.ParseStringToPathFigureCollection(figures, glyph.Path);
        _path.Data = new PathGeometry { Figures = figures };
        _path.RenderTransform = new ScaleTransform { ScaleX = scale, ScaleY = scale };
        WidthRequest = Size;
        HeightRequest = Size;
    }

    private readonly record struct Glyph(string Path, double ViewBox);

    // FA 6.7.2 Free solid path data, identical sources to the icon_*.svg rasters AppIcon bakes.
    private static readonly Dictionary<AppIconGlyph, Glyph> GlyphData = new()
    {
        [AppIconGlyph.ProjectsList] = new(
            "M152.1 38.2c9.9 8.9 10.7 24 1.8 33.9l-72 80c-4.4 4.9-10.6 7.8-17.2 7.9s-12.9-2.4-17.6-7L7 113C-2.3 103.6-2.3 88.4 7 79s24.6-9.4 33.9 0l22.1 22.1 55.1-61.2c8.9-9.9 24-10.7 33.9-1.8zm0 160c9.9 8.9 10.7 24 1.8 33.9l-72 80c-4.4 4.9-10.6 7.8-17.2 7.9s-12.9-2.4-17.6-7L7 273c-9.4-9.4-9.4-24.6 0-33.9s24.6-9.4 33.9 0l22.1 22.1 55.1-61.2c8.9-9.9 24-10.7 33.9-1.8zM224 96c0-17.7 14.3-32 32-32l224 0c17.7 0 32 14.3 32 32s-14.3 32-32 32l-224 0c-17.7 0-32-14.3-32-32zm0 160c0-17.7 14.3-32 32-32l224 0c17.7 0 32 14.3 32 32s-14.3 32-32 32l-224 0c-17.7 0-32-14.3-32-32zM160 416c0-17.7 14.3-32 32-32l288 0c17.7 0 32 14.3 32 32s-14.3 32-32 32l-288 0c-17.7 0-32-14.3-32-32zM48 368a48 48 0 1 1 0 96 48 48 0 1 1 0-96z",
            512),
        [AppIconGlyph.Search] = new(
            "M416 208c0 45.9-14.9 88.3-40 122.7L502.6 457.4c12.5 12.5 12.5 32.8 0 45.3s-32.8 12.5-45.3 0L330.7 376c-34.4 25.2-76.8 40-122.7 40C93.1 416 0 322.9 0 208S93.1 0 208 0S416 93.1 416 208zM208 352a144 144 0 1 0 0-288 144 144 0 1 0 0 288z",
            512),
        [AppIconGlyph.Gear] = new(
            "M495.9 166.6c3.2 8.7 .5 18.4-6.4 24.6l-43.3 39.4c1.1 8.3 1.7 16.8 1.7 25.4s-.6 17.1-1.7 25.4l43.3 39.4c6.9 6.2 9.6 15.9 6.4 24.6c-4.4 11.9-9.7 23.3-15.8 34.3l-4.7 8.1c-6.6 11-14 21.4-22.1 31.2c-5.9 7.2-15.7 9.6-24.5 6.8l-55.7-17.7c-13.4 10.3-28.2 18.9-44 25.4l-12.5 57.1c-2 9.1-9 16.3-18.2 17.8c-13.8 2.3-28 3.5-42.5 3.5s-28.7-1.2-42.5-3.5c-9.2-1.5-16.2-8.7-18.2-17.8l-12.5-57.1c-15.8-6.5-30.6-15.1-44-25.4L83.1 425.9c-8.8 2.8-18.6 .3-24.5-6.8c-8.1-9.8-15.5-20.2-22.1-31.2l-4.7-8.1c-6.1-11-11.4-22.4-15.8-34.3c-3.2-8.7-.5-18.4 6.4-24.6l43.3-39.4C64.6 273.1 64 264.6 64 256s.6-17.1 1.7-25.4L22.4 191.2c-6.9-6.2-9.6-15.9-6.4-24.6c4.4-11.9 9.7-23.3 15.8-34.3l4.7-8.1c6.6-11 14-21.4 22.1-31.2c5.9-7.2 15.7-9.6 24.5-6.8l55.7 17.7c13.4-10.3 28.2-18.9 44-25.4l12.5-57.1c2-9.1 9-16.3 18.2-17.8C227.3 1.2 241.5 0 256 0s28.7 1.2 42.5 3.5c9.2 1.5 16.2 8.7 18.2 17.8l12.5 57.1c15.8 6.5 30.6 15.1 44 25.4l55.7-17.7c8.8-2.8 18.6-.3 24.5 6.8c8.1 9.8 15.5 20.2 22.1 31.2l4.7 8.1c6.1 11 11.4 22.4 15.8 34.3zM256 336a80 80 0 1 0 0-160 80 80 0 1 0 0 160z",
            512),
        [AppIconGlyph.Info] = new(
            "M256 512A256 256 0 1 0 256 0a256 256 0 1 0 0 512zM216 336l24 0 0-64-24 0c-13.3 0-24-10.7-24-24s10.7-24 24-24l48 0c13.3 0 24 10.7 24 24l0 88 8 0c13.3 0 24 10.7 24 24s-10.7 24-24 24l-80 0c-13.3 0-24-10.7-24-24s10.7-24 24-24zm40-208a32 32 0 1 1 0 64 32 32 0 1 1 0-64z",
            512),
        [AppIconGlyph.Bell] = new(
            "M224 0c-17.7 0-32 14.3-32 32l0 19.2C119 66 64 130.6 64 208l0 25.4c0 45.4-15.5 89.5-43.8 124.9L5.3 377c-5.8 7.2-6.9 17.1-2.9 25.4S14.8 416 24 416l400 0c9.2 0 17.6-5.3 21.6-13.6s2.9-18.2-2.9-25.4l-14.9-18.6C399.5 322.9 384 278.8 384 233.4l0-25.4c0-77.4-55-142-128-156.8L256 32c0-17.7-14.3-32-32-32zm0 96c61.9 0 112 50.1 112 112l0 25.4c0 47.9 13.9 94.6 39.7 134.6L72.3 368C98.1 328 112 281.3 112 233.4l0-25.4c0-61.9 50.1-112 112-112zm64 352l-64 0-64 0c0 17 6.7 33.3 18.7 45.3s28.3 18.7 45.3 18.7s33.3-6.7 45.3-18.7s18.7-28.3 18.7-45.3z",
            448),
        [AppIconGlyph.Moon] = new(
            "M144.7 98.7c-21 34.1-33.1 74.3-33.1 117.3c0 98 62.8 181.4 150.4 211.7c-12.4 2.8-25.3 4.3-38.6 4.3C126.6 432 48 353.3 48 256c0-68.9 39.4-128.4 96.8-157.3zm62.1-66C91.1 41.2 0 137.9 0 256C0 379.7 100 480 223.5 480c47.8 0 92-15 128.4-40.6c1.9-1.3 3.7-2.7 5.5-4c4.8-3.6 9.4-7.4 13.9-11.4c2.7-2.4 5.3-4.8 7.9-7.3c5-4.9 6.3-12.5 3.1-18.7s-10.1-9.7-17-8.5c-3.7 .6-7.4 1.2-11.1 1.6c-5 .5-10.1 .9-15.3 1c-1.2 0-2.5 0-3.7 0l-.3 0c-96.8-.2-175.2-78.9-175.2-176c0-54.8 24.9-103.7 64.1-136c1-.9 2.1-1.7 3.2-2.6c4-3.2 8.2-6.2 12.5-9c3.1-2 6.3-4 9.6-5.8c6.1-3.5 9.2-10.5 7.7-17.3s-7.3-11.9-14.3-12.5c-3.6-.3-7.1-.5-10.7-.6c-2.7-.1-5.5-.1-8.2-.1c-3.3 0-6.5 .1-9.8 .2c-2.3 .1-4.6 .2-6.9 .4z",
            384),
        [AppIconGlyph.Sun] = new(
            "M302.5 80.6l27-27c6.2-6.2 6.2-16.4 0-22.6s-16.4-6.2-22.6 0l-27 27c-6.2 6.2-6.2 16.4 0 22.6s16.4 6.2 22.6 0zM384 256a128 128 0 1 1-256 0 128 128 0 1 1 256 0zM224 16c-8.8 0-16 7.2-16 16l0 48c0 8.8 7.2 16 16 16s16-7.2 16-16l0-48c0-8.8-7.2-16-16-16zM91.3 107.3c6.2-6.2 6.2-16.4 0-22.6s-16.4-6.2-22.6 0l-27 27c-6.2 6.2-6.2 16.4 0 22.6s16.4 6.2 22.6 0l27-27zM64 240c-8.8 0-16 7.2-16 16s7.2 16 16 16l48 0c8.8 0 16-7.2 16-16s-7.2-16-16-16l-48 0zM53.4 403.3l27-27c6.2-6.2 6.2-16.4 0-22.6s-16.4-6.2-22.6 0l-27 27c-6.2 6.2-6.2 16.4 0 22.6s16.4 6.2 22.6 0zM256 384c-8.8 0-16 7.2-16 16l0 48c0 8.8 7.2 16 16 16s16-7.2 16-16l0-48c0-8.8-7.2-16-16-16zM420.7 404.7c6.2 6.2 16.4 6.2 22.6 0l27-27c6.2-6.2 6.2-16.4 0-22.6s-16.4-6.2-22.6 0l-27 27c-6.2 6.2-6.2 16.4 0 22.6zM464 224l-48 0c-8.8 0-16 7.2-16 16s7.2 16 16 16l48 0c8.8 0 16-7.2 16-16s-7.2-16-16-16zM394.5 139.3c3.1 3.1 7.2 4.7 11.3 4.7s8.2-1.6 11.3-4.7l27-27c6.2-6.2 6.2-16.4 0-22.6s-16.4-6.2-22.6 0l-27 27c-6.2 6.2-6.2 16.4 0 22.6z",
            512),
    };
}
