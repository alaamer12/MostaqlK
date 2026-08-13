using MostaqlK.UI.PlatformComponents;

namespace MostaqlK.UI.DesignSystem;

/// <summary>
/// Diagonal, animated "new" corner ribbon overlaid on the physical-left corner of an unread
/// <see cref="PlatformComponents.AppCard"/> (the app runs <c>FlowDirection="RightToLeft"</c>, so
/// "physical left" is the layout's <c>End</c> edge, opposite the card's existing inline-start
/// accent border in <see cref="PlatformComponents.AppCard.IsUnread"/>). Carries a looping shimmer
/// sweep — the same "sweep a translucent highlight across the surface" idea as
/// <see cref="ShimmerBox"/>, reused here as the "ribbled" motion effect the design calls for —
/// so a fresh, unread project visibly stands out from the rest of the feed at a glance.
/// Respects <see cref="MotionPreferences.IsReducedMotionRequested"/> (shows the ribbon as a
/// static badge, no sweep, when the user has asked for reduced motion) and re-colors itself for
/// light/dark theme, same pattern as <see cref="PlatformComponents.AppCard"/>.
/// </summary>
public class NewRibbonBadge : Grid
{
    public static readonly BindableProperty IsActiveProperty = BindableProperty.Create(
        nameof(IsActive),
        typeof(bool),
        typeof(NewRibbonBadge),
        defaultValue: false,
        propertyChanged: OnIsActiveChanged);

    /// <summary>Whether the owning card is unread — shows the ribbon and (re)starts its shimmer sweep loop.</summary>
    public bool IsActive
    {
        get => (bool)GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    private readonly BoxView _background;
    private readonly BoxView _sweep;
    private readonly Label _label;
    private bool _isAnimating;

    public NewRibbonBadge()
    {
        InputTransparent = true;
        HorizontalOptions = LayoutOptions.Start;
        VerticalOptions = LayoutOptions.Start;
        WidthRequest = 96;
        HeightRequest = 26;
        IsClippedToBounds = true;
        IsVisible = false;

        // Sits diagonally across the card's physical-left corner, extending slightly past the
        // edges so the ribbon reads as "wrapping the corner" instead of a floating rectangle.
        Rotation = -45;
        AnchorX = 0.5;
        AnchorY = 0.5;
        TranslationX = -30;
        TranslationY = 10;

        _background = new BoxView();

        _sweep = new BoxView
        {
            WidthRequest = 28,
            Opacity = 0.75,
            Color = Color.FromArgb("#66FFFFFF"),
            TranslationX = -40,
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

        Loaded += (_, _) => RestartSweepIfNeeded();
        Unloaded += (_, _) => _isAnimating = false;

        if (Application.Current is { } app)
        {
            app.RequestedThemeChanged += (_, _) => UpdateThemeColors();
        }
    }

    private static void OnIsActiveChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var badge = (NewRibbonBadge)bindable;
        badge.IsVisible = (bool)newValue;
        badge.RestartSweepIfNeeded();
    }

    private void UpdateThemeColors()
    {
        var isDark = Application.Current?.RequestedTheme == AppTheme.Dark;
        _background.Color = isDark ? Color.FromArgb("#5CA8DE") : Color.FromArgb("#2386C8");
    }

    private async void RestartSweepIfNeeded()
    {
        if (!IsActive || !IsLoaded || _isAnimating)
        {
            return;
        }

        _isAnimating = true;
        try
        {
            while (_isAnimating && IsActive && IsLoaded)
            {
                if (MotionPreferences.IsReducedMotionRequested)
                {
                    // Static badge only - no motion - honoring the reduced-motion preference.
                    _sweep.TranslationX = -40;
                    await Task.Delay(500);
                    continue;
                }

                _sweep.TranslationX = -40;
                await _sweep.TranslateToAsync(Width + 40, 0, 1500, Easing.CubicInOut);
                await Task.Delay(900);
            }
        }
        finally
        {
            _isAnimating = false;
        }
    }
}
