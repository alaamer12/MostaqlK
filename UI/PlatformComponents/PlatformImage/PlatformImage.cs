using Microsoft.Maui.Controls;

namespace MostaqlK.UI.PlatformComponents;

/// <summary>
/// Base unit generalizing the <see cref="PlatformSelect"/>/<c>_X.{Family}.cs</c> per-platform
/// pattern (already used by <see cref="MostaqlK.UI.DesignSystem.PressableEffect"/>) to images and
/// icons: a call site declares a *set* of candidate sources — one per platform, or one shared by
/// an entire OS family via <see cref="MobileSource"/> — and this unit resolves the correct one for
/// the current compile-time target exactly once, then caches (memoizes) it.
///
/// Why this exists rather than relying on MAUI's built-in image catalog: the standard
/// <c>Resources/Images</c>/<c>MauiImage</c> pipeline only overrides an image by density/file-path
/// per <c>TargetFramework</c> (e.g. a platform-specific <c>drawable-xhdpi</c> file silently wins
/// over the shared SVG) — it has no notion of a *bindable, code-level* "this image is
/// compositionally different per platform" source, which is what call sites like Onboarding's
/// step illustrations need once real per-platform art exists. See the Microsoft Learn
/// single-project multi-targeting docs and the dotnet/maui platform-specific-image-resource
/// discussion (checked before writing this class) — neither covers this case.
///
/// Resolution/caching: the resolved <see cref="ImageSource"/> is computed via
/// <see cref="PlatformSelect.For{T}"/> and stored in a private field, invalidated ONLY when one of
/// the source bindable properties actually changes — never on layout/measure/re-render, so
/// repeated arrange passes (e.g. during the onboarding step-transition animation) never re-resolve
/// or flicker the image.
///
/// Family-sharing convention: <see cref="AndroidSource"/>/<see cref="IOSSource"/> each override
/// <see cref="MobileSource"/> only when explicitly set; when left unset, both platforms fall back
/// to the shared family value, mirroring how <c>PressableEffect.Android.cs</c>/<c>.iOS.cs</c> both
/// export <c>_PressableEffect.Mobile.cs</c>'s behavior without duplicating it.
/// </summary>
public partial class PlatformImage : ContentView
{
    private readonly Image _image = new()
    {
        Aspect = Aspect.AspectFit,
        HorizontalOptions = LayoutOptions.Fill,
        VerticalOptions = LayoutOptions.Fill,
    };

    private ImageSource? _resolvedCache;
    private bool _dirty = true;

    public static readonly BindableProperty WindowsSourceProperty =
        BindableProperty.Create(nameof(WindowsSource), typeof(ImageSource), typeof(PlatformImage), null,
            propertyChanged: OnAnySourceChanged);

    public static readonly BindableProperty MobileSourceProperty =
        BindableProperty.Create(nameof(MobileSource), typeof(ImageSource), typeof(PlatformImage), null,
            propertyChanged: OnAnySourceChanged);

    public static readonly BindableProperty AndroidSourceProperty =
        BindableProperty.Create(nameof(AndroidSource), typeof(ImageSource), typeof(PlatformImage), null,
            propertyChanged: OnAnySourceChanged);

    public static readonly BindableProperty IOSSourceProperty =
        BindableProperty.Create(nameof(IOSSource), typeof(ImageSource), typeof(PlatformImage), null,
            propertyChanged: OnAnySourceChanged);

    public static readonly BindableProperty MacCatalystSourceProperty =
        BindableProperty.Create(nameof(MacCatalystSource), typeof(ImageSource), typeof(PlatformImage), null,
            propertyChanged: OnAnySourceChanged);

    public static readonly BindableProperty DefaultSourceProperty =
        BindableProperty.Create(nameof(DefaultSource), typeof(ImageSource), typeof(PlatformImage), null,
            propertyChanged: OnAnySourceChanged);

    public static readonly BindableProperty AspectProperty =
        BindableProperty.Create(nameof(Aspect), typeof(Aspect), typeof(PlatformImage), Aspect.AspectFit,
            propertyChanged: OnAspectChanged);

    /// <summary>Source used only when compiling for Windows.</summary>
    public ImageSource? WindowsSource
    {
        get => (ImageSource?)GetValue(WindowsSourceProperty);
        set => SetValue(WindowsSourceProperty, value);
    }

    /// <summary>Shared fallback for Android + iOS (the "mobile" OS family) when a more specific per-OS source isn't set.</summary>
    public ImageSource? MobileSource
    {
        get => (ImageSource?)GetValue(MobileSourceProperty);
        set => SetValue(MobileSourceProperty, value);
    }

    /// <summary>Overrides <see cref="MobileSource"/> for Android specifically, when set.</summary>
    public ImageSource? AndroidSource
    {
        get => (ImageSource?)GetValue(AndroidSourceProperty);
        set => SetValue(AndroidSourceProperty, value);
    }

    /// <summary>Overrides <see cref="MobileSource"/> for iOS specifically, when set.</summary>
    public ImageSource? IOSSource
    {
        get => (ImageSource?)GetValue(IOSSourceProperty);
        set => SetValue(IOSSourceProperty, value);
    }

    /// <summary>Overrides <see cref="MobileSource"/> for Mac Catalyst specifically, when set.</summary>
    public ImageSource? MacCatalystSource
    {
        get => (ImageSource?)GetValue(MacCatalystSourceProperty);
        set => SetValue(MacCatalystSourceProperty, value);
    }

    /// <summary>Fallback used when no source resolves for the current platform (fail-safe, avoids a crash/blank binding).</summary>
    public ImageSource? DefaultSource
    {
        get => (ImageSource?)GetValue(DefaultSourceProperty);
        set => SetValue(DefaultSourceProperty, value);
    }

    public Aspect Aspect
    {
        get => (Aspect)GetValue(AspectProperty);
        set => SetValue(AspectProperty, value);
    }

    public PlatformImage()
    {
        Content = _image;
    }

    private static void OnAnySourceChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var self = (PlatformImage)bindable;
        self._dirty = true;
        self.ApplyResolvedSource();
    }

    private static void OnAspectChanged(BindableObject bindable, object oldValue, object newValue)
    {
        ((PlatformImage)bindable)._image.Aspect = (Aspect)newValue;
    }

    /// <summary>Resolves (memoized) and applies the source for the current platform. Only recomputes when a source property actually changed since the last resolution.</summary>
    private void ApplyResolvedSource()
    {
        if (_dirty)
        {
            _resolvedCache = Resolve();
            _dirty = false;
        }

        _image.Source = _resolvedCache;
    }

    private ImageSource? Resolve()
    {
        var perFamily = PlatformSelect.For(
            android: AndroidSource ?? MobileSource,
            ios: IOSSource ?? MobileSource,
            windows: WindowsSource,
            macCatalyst: MacCatalystSource ?? MobileSource);

        return perFamily ?? DefaultSource;
    }
}
