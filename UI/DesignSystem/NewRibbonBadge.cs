using MostaqlK.UI.PlatformComponents;

namespace MostaqlK.UI.DesignSystem;

/// <summary>
/// Diagonal "جديد" corner ribbon overlaid on the physical-left corner of an unread feed row
/// (the app runs <c>FlowDirection="RightToLeft"</c>, so "physical left" is the layout's <c>End</c>
/// edge). A fresh, unread project visibly stands out from the rest of the feed at a glance, and the
/// ribbon CROSS-FADES OUT (not cuts) when the row flips to read — single tap or "تحديد الكل كمقروء".
/// Its brand-colored surface carries the SAME brushed-metal "chrome" sheen as the today-counter
/// (<see cref="ChromeValueLabel"/> / <c>projects.html .fx-chrome</c>): a specular band swept across
/// the fill, painted into the native surface by one reused WinUI gradient brush and advanced by a
/// single shared, gated 33 ms ticker (animation-safety.md — no per-frame TranslateTo*/Storyboard/
/// RenderTransform churn, zero allocations on the hot path). The Windows half lives in
/// <c>NewRibbonBadge.Windows.cs</c>; on platforms without that partial the ribbon is a plain flat
/// brand fill (no sheen), so the shared file never branches on a platform.
/// Re-colors itself for light/dark theme and follows the active source's brand palette, same
/// pattern as <see cref="PlatformComponents.AppCard"/>.
/// </summary>
public partial class NewRibbonBadge : Grid
{
    public static readonly BindableProperty IsActiveProperty = BindableProperty.Create(
        nameof(IsActive),
        typeof(bool),
        typeof(NewRibbonBadge),
        defaultValue: false,
        propertyChanged: OnIsActiveChanged);

    /// <summary>Whether the owning card is unread — shows the ribbon and (re)starts its chrome sweep.</summary>
    public bool IsActive
    {
        get => (bool)GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    /// <summary>Length of the deactivation cross-fade (reduced motion collapses it to 0).</summary>
    private const uint DeactivateFadeMs = 220;

    private readonly RibbonSurface _background;
    private readonly Label _label;
    private int _deactivationGeneration;

    public NewRibbonBadge()
    {
        InputTransparent = true;
        HorizontalOptions = LayoutOptions.Start;
        VerticalOptions = LayoutOptions.Start;
        WidthRequest = 84;
        HeightRequest = 24;
        IsClippedToBounds = true;
        IsVisible = false;

        // Diagonal band across the row's top-LEFT corner, rotated -135° (the original -45 turned
        // a further -90 anti-clockwise per the owner) so it descends the other way; kept ENTIRELY
        // inside the card — the 84×24 box at -135° still has a ≈38 px half-extent, so the small
        // inward nudge + vertical drop clear the left/top edges.
        Rotation = 45;
        AnchorX = 0.5;
        AnchorY = 0.5;
        TranslationX = 20;
        TranslationY = 12;

        // The chrome surface: a BoxView the Windows half paints with the animated gradient brush.
        // The shared file only ever sets its flat brand Color (the non-Windows fill and the
        // source-of-truth the Windows brush samples its base ink from).
        _background = new RibbonSurface(this);

        _label = new Label
        {
            Text = "جديد",
            FontFamily = "TajawalBold",
            FontSize = 11,
            TextColor = Colors.White,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            // Counter-rotate the glyphs back to upright-ish reading regardless of the outer
            // RTL FlowDirection — the ribbon's own -45° Rotation already handles the diagonal.
            FlowDirection = FlowDirection.MatchParent,
        };

        Children.Add(_background);
        Children.Add(_label);

        UpdateThemeColors();
        InitializeChrome();

        Loaded += OnBadgeLoaded;
        Unloaded += OnBadgeUnloaded;
    }

    private void OnBadgeLoaded(object? sender, EventArgs e)
    {
        // Application is an app-lifetime publisher, so a constructor subscription would root every
        // badge this process ever realizes; the ribbon only needs to react while it is on screen.
        if (Application.Current is { } app)
        {
            app.RequestedThemeChanged -= OnRequestedThemeChanged;
            app.RequestedThemeChanged += OnRequestedThemeChanged;
        }

        // The ribbon background is the live brand fill, so a source morph repaints it too —
        // same on-screen lifetime as the theme subscription above.
        BrandTokens.BrandPalette.PaletteChanged -= UpdateThemeColors;
        BrandTokens.BrandPalette.PaletteChanged += UpdateThemeColors;

        UpdateThemeColors();
        RegisterChrome();
    }

    private void OnBadgeUnloaded(object? sender, EventArgs e)
    {
        if (Application.Current is { } app)
        {
            app.RequestedThemeChanged -= OnRequestedThemeChanged;
        }

        BrandTokens.BrandPalette.PaletteChanged -= UpdateThemeColors;
        UnregisterChrome();
    }

    private void OnRequestedThemeChanged(object? sender, AppThemeChangedEventArgs e) => UpdateThemeColors();

    private static void OnIsActiveChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var badge = (NewRibbonBadge)bindable;
        if ((bool)newValue)
        {
            badge._deactivationGeneration++;
            badge.CancelAnimations();
            badge.Opacity = 1;
            badge.IsVisible = true;
        }
        else
        {
            _ = badge.DeactivateWithFadeAsync();
        }
    }

    /// <summary>Cross-fade the ribbon out when its row becomes read, then collapse it. A re-activation
    /// during the fade (container recycling onto another unread project) bumps the generation and wins.</summary>
    private async Task DeactivateWithFadeAsync()
    {
        var generation = ++_deactivationGeneration;
        var duration = MotionPreferences.IsReducedMotionRequested ? 0u : DeactivateFadeMs;

        await this.FadeToAsync(0, duration, Easing.CubicIn);

        if (generation != _deactivationGeneration || IsActive)
        {
            return;
        }

        IsVisible = false;
    }

    private void UpdateThemeColors()
    {
        // Brand fill for the active source + current theme (was the inline Mostaql light/dark pair).
        // On Windows this Color write is what the chrome brush samples its base ink from — the
        // appended BoxViewHandler mapping re-paints the (re-colored) gradient in the same pass.
        _background.Color = BrandTokens.BrandPalette.Brand;
    }

    /// <summary>
    /// The chrome surface. A dedicated <see cref="BoxView"/> subclass so the Windows half's
    /// appended <c>BoxViewHandler</c> mapping can recognize exactly this element (and reach its
    /// owning badge) without touching every other <c>BoxView</c> in the app — the same
    /// "recognize the instance, re-apply the brush" seam <see cref="ChromeValueLabel"/> uses for
    /// <c>Label.TextColor</c>. Cross-platform it behaves as a plain BoxView.
    /// </summary>
    private sealed class RibbonSurface : BoxView
    {
        public RibbonSurface(NewRibbonBadge owner) => Owner = owner;

        public NewRibbonBadge Owner { get; }
    }

    // Chrome seams — implemented in NewRibbonBadge.Windows.cs; no-ops where no partial exists.
    partial void InitializeChrome();
    partial void RegisterChrome();
    partial void UnregisterChrome();
}
