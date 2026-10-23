using MostaqlK.UI.PlatformComponents;

namespace MostaqlK.UI.DesignSystem;

/// <summary>
/// Diagonal "جديد" corner ribbon overlaid on the physical-left corner of an unread feed row
/// (the app runs <c>FlowDirection="RightToLeft"</c>, so "physical left" is the layout's <c>End</c>
/// edge). A fresh, unread project visibly stands out from the rest of the feed at a glance, and the
/// ribbon CROSS-FADES OUT (not cuts) when the row flips to read — single tap or "تحديد الكل كمقروء".
/// Carries a soft light-band pulse: one shared, gated 33 ms ticker (the ChromeValueLabel contract,
/// see animation-safety.md) drives ONLY the sweep's Opacity — no per-frame TranslateTo*/Storyboard/
/// RenderTransform churn, zero allocations on the hot path. The ticker path additionally parks on
/// reduced motion (incl. tray-hidden ambient suspension), on any invisible ancestor, and whenever
/// the badge is inactive or unloaded, so backgrounded/hidden windows do no render work.
/// Re-colors itself for light/dark theme and follows the active source's brand palette, same
/// pattern as <see cref="PlatformComponents.AppCard"/>.
/// </summary>
public class NewRibbonBadge : Grid
{
    public static readonly BindableProperty IsActiveProperty = BindableProperty.Create(
        nameof(IsActive),
        typeof(bool),
        typeof(NewRibbonBadge),
        defaultValue: false,
        propertyChanged: OnIsActiveChanged);

    /// <summary>Whether the owning card is unread — shows the ribbon and (re)starts its light pulse.</summary>
    public bool IsActive
    {
        get => (bool)GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    /// <summary>One pulse period — matches the retired sweep loop (1.5 s crossing + 0.9 s rest).</summary>
    private const double CycleDurationMs = 2400;

    /// <summary>The band breathes during the first 1.5 s of the cycle, then rests invisible.</summary>
    private const double PulseDurationMs = 1500;

    /// <summary>Peak opacity of the light band (the old sweep's 0.75 constant).</summary>
    private const double PulsePeakOpacity = 0.75;

    /// <summary>Length of the deactivation cross-fade (reduced motion collapses it to 0).</summary>
    private const uint DeactivateFadeMs = 220;

    /// <summary>All loaded badges, pulsed by the single <see cref="_sharedTicker"/>.</summary>
    private static readonly List<NewRibbonBadge> ActiveBadges = [];
    private static IDispatcherTimer? _sharedTicker;
    private static long _startTicks;

    private readonly BoxView _background;
    private readonly BoxView _sweep;
    private readonly Label _label;
    private bool _registered;
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

        _background = new BoxView();

        // Static light band at the ribbon's centre; the shared ticker pulses its Opacity only
        // (no transform animation — the retired TranslateToAsync loop violated animation-safety).
        _sweep = new BoxView
        {
            WidthRequest = 28,
            Opacity = 0,
            Color = Color.FromArgb("#66FFFFFF"),
            HorizontalOptions = LayoutOptions.Center,
        };

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
        Children.Add(_sweep);
        Children.Add(_label);

        UpdateThemeColors();

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
        Register();
    }

    private void OnBadgeUnloaded(object? sender, EventArgs e)
    {
        if (Application.Current is { } app)
        {
            app.RequestedThemeChanged -= OnRequestedThemeChanged;
        }

        BrandTokens.BrandPalette.PaletteChanged -= UpdateThemeColors;
        Unregister();
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
        _background.Color = BrandTokens.BrandPalette.Brand;
    }

    private void Register()
    {
        if (_registered)
        {
            return;
        }

        _registered = true;
        lock (ActiveBadges)
        {
            ActiveBadges.Add(this);
        }

        EnsureSharedTickerRunning();
    }

    private void Unregister()
    {
        if (!_registered)
        {
            return;
        }

        _registered = false;
        lock (ActiveBadges)
        {
            ActiveBadges.Remove(this);
        }
    }

    /// <summary>
    /// Advances this badge by one shared-ticker frame. Never throws outward: the badge can be
    /// detached/recycled between ticks, so a stale reference hitting a torn-down handler must be
    /// tolerated (same contract as ChromeValueLabel.Tick / ShimmerBox.Tick).
    /// </summary>
    private void Tick(double nowMs)
    {
        try
        {
            if (!IsActive || !IsLoaded || !IsEffectivelyVisible() || MotionPreferences.IsReducedMotionRequested)
            {
                _sweep.Opacity = 0;
                return;
            }

            var elapsedInCycle = nowMs % CycleDurationMs;
            if (elapsedInCycle < 0)
            {
                elapsedInCycle += CycleDurationMs;
            }

            // First 62.5%: a sine breath up and back (smooth in, smooth out); the rest: rest state.
            _sweep.Opacity = elapsedInCycle >= PulseDurationMs
                ? 0
                : PulsePeakOpacity * Math.Sin(Math.PI * (elapsedInCycle / PulseDurationMs));
        }
        catch (Exception)
        {
            // Purely decorative — a stale/disposed badge must never surface as a crash.
            Unregister();
        }
    }

    /// <summary>
    /// True when this badge and every ancestor up to the page are visible. MAUI maps
    /// <c>IsVisible=false</c> to a collapsed platform element that KEEPS its children attached,
    /// so a per-instance gate on this badge's own flag alone would leave hidden ancestors ticking.
    /// </summary>
    private bool IsEffectivelyVisible()
    {
        for (Element? node = this; node is not null; node = node.Parent)
        {
            if (node is VisualElement visual && !visual.IsVisible)
            {
                return false;
            }
        }

        return true;
    }

    private static void EnsureSharedTickerRunning()
    {
        if (_sharedTicker is not null || Application.Current?.Dispatcher is not { } dispatcher)
        {
            return;
        }

        _startTicks = Environment.TickCount64;
        _sharedTicker = dispatcher.CreateTimer();
        _sharedTicker.Interval = TimeSpan.FromMilliseconds(33); // ~30fps — smooth enough for a slow pulse.
        _sharedTicker.Tick += OnSharedTickerTick;
        _sharedTicker.Start();
    }

    private static void OnSharedTickerTick(object? sender, EventArgs e)
    {
        var nowMs = (double)(Environment.TickCount64 - _startTicks);

        // Backwards so a badge that unregisters itself inside Tick (stale catch) never shifts an
        // index out from under this loop; no ToArray() snapshot allocation per frame.
        lock (ActiveBadges)
        {
            for (var i = ActiveBadges.Count - 1; i >= 0; i--)
            {
                ActiveBadges[i].Tick(nowMs);
            }
        }
    }
}
