namespace MostaqlK.UI.DesignSystem;

/// <summary>
/// Animated shimmer placeholder used for skeleton loading states, paired 1:1 with every content
/// element while data is loading (see system-components.md, section 13.3). Renders a rounded
/// flat box with a translucent overlay that sweeps left-to-right on a loop while attached to the
/// visual tree, approximating the "SkeletonBase/SkeletonShimmer" color tokens until the full
/// Design System token set lands.
/// </summary>
public class ShimmerBox : ContentView
{
    private readonly BoxView _baseBox;
    private readonly BoxView _sweep;
    private bool _isAnimating;

    public ShimmerBox()
    {
        _baseBox = new BoxView
        {
            Color = Color.FromArgb("#E0E0E0"),
            CornerRadius = 8,
        };

        _sweep = new BoxView
        {
            Color = Color.FromArgb("#40FFFFFF"),
            CornerRadius = 8,
            WidthRequest = 60,
            Opacity = 0.6,
        };

        Content = new Grid { Children = { _baseBox, _sweep } };

        Loaded += (_, _) => StartShimmer();
        Unloaded += (_, _) => _isAnimating = false;
    }

    private async void StartShimmer()
    {
        if (_isAnimating)
        {
            return;
        }

        _isAnimating = true;
        while (_isAnimating)
        {
            var travel = Math.Max(0, Width - _sweep.Width);
            await _sweep.TranslateToAsync(travel, 0, 1400, Easing.Linear);
            _sweep.TranslationX = 0;
            await Task.Delay(200);
        }
    }
}
