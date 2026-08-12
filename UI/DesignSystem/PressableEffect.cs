using System.Windows.Input;
using MostaqlK.UI.PlatformComponents;

namespace MostaqlK.UI.DesignSystem;

/// <summary>
/// A shared behavior that adds standard pressing and hovering effects to any View:
/// 1. Scaling (0.98) and Opacity (0.8) change on press.
/// 2. Hover background highlight (subtle or shimmer) and pointer cursor on hover.
/// Honours <see cref="MotionPreferences"/> for scaling animations.
/// </summary>
public partial class PressableEffect : Behavior<View>
{
    public static readonly BindableProperty HoverColorProperty =
        BindableProperty.Create(nameof(HoverColor), typeof(Color), typeof(PressableEffect), null);

    public Color? HoverColor
    {
        get => (Color?)GetValue(HoverColorProperty);
        set => SetValue(HoverColorProperty, value);
    }

    public static readonly BindableProperty ApplyHoverHighlightProperty =
        BindableProperty.Create(nameof(ApplyHoverHighlight), typeof(bool), typeof(PressableEffect), true);

    public bool ApplyHoverHighlight
    {
        get => (bool)GetValue(ApplyHoverHighlightProperty);
        set => SetValue(ApplyHoverHighlightProperty, value);
    }

    public static readonly BindableProperty PressedScaleProperty =
        BindableProperty.Create(nameof(PressedScale), typeof(double), typeof(PressableEffect), 0.98);

    public double PressedScale
    {
        get => (double)GetValue(PressedScaleProperty);
        set => SetValue(PressedScaleProperty, value);
    }

    public static readonly BindableProperty PressedOpacityProperty =
        BindableProperty.Create(nameof(PressedOpacity), typeof(double), typeof(PressableEffect), 0.8);

    public double PressedOpacity
    {
        get => (double)GetValue(PressedOpacityProperty);
        set => SetValue(PressedOpacityProperty, value);
    }

    private View? _associatedView;
    private Color? _originalBackgroundColor;
    private PointerGestureRecognizer? _pointerRecognizer;
    private TapGestureRecognizer? _tapRecognizer;

    protected override void OnAttachedTo(View bindable)
    {
        base.OnAttachedTo(bindable);
        _associatedView = bindable;

        _pointerRecognizer = new PointerGestureRecognizer();
        _pointerRecognizer.PointerEntered += OnPointerEntered;
        _pointerRecognizer.PointerExited += OnPointerExited;
        _pointerRecognizer.PointerPressed += OnPointerPressed;
        _pointerRecognizer.PointerReleased += OnPointerReleased;
        
        bindable.GestureRecognizers.Add(_pointerRecognizer);
        
        _tapRecognizer = new TapGestureRecognizer();
        _tapRecognizer.Tapped += (s, e) => ResetPressEffect();
        bindable.GestureRecognizers.Add(_tapRecognizer);

        bindable.HandlerChanged += OnHandlerChanged;
    }

    protected override void OnDetachingFrom(View bindable)
    {
        base.OnDetachingFrom(bindable);
        if (_pointerRecognizer != null)
        {
            bindable.GestureRecognizers.Remove(_pointerRecognizer);
        }
        if (_tapRecognizer != null)
        {
            bindable.GestureRecognizers.Remove(_tapRecognizer);
        }
        bindable.HandlerChanged -= OnHandlerChanged;
        _associatedView = null;
    }

    private void OnHandlerChanged(object? sender, EventArgs e)
    {
        if (_associatedView?.Handler != null)
        {
            _cursorApplied = false;
        }
    }

    partial void ApplyPlatformCursor();

    private void OnPointerEntered(object? sender, PointerEventArgs e)
    {
        if (_associatedView == null) return;

        if (ApplyHoverHighlight)
        {
            // Always use current color as base for hover, but avoid using a previous hover color
            var currentColor = _associatedView.BackgroundColor ?? Colors.Transparent;
            
            // If we are already hovered, don't re-store original color
            if (_originalBackgroundColor == null)
            {
                _originalBackgroundColor = currentColor;
            }
            
            // Modern elegant hover color: slightly lighter in dark, slightly darker in light
            var defaultHover = Application.Current?.RequestedTheme == AppTheme.Dark
                ? Color.FromArgb("#15FFFFFF") // Subtle light overlay for dark theme
                : Color.FromArgb("#0A000000"); // Very subtle dark overlay for light theme
            
            var highlight = HoverColor ?? defaultHover;
            
            // If the view has a background, we should blend the highlight
            if (_originalBackgroundColor != Colors.Transparent && _originalBackgroundColor != null)
            {
                _associatedView.BackgroundColor = BlendColors(_originalBackgroundColor, highlight);
            }
            else
            {
                _associatedView.BackgroundColor = highlight;
            }
        }

        // Apply cursor only once
        if (!_cursorApplied)
        {
            ApplyPlatformCursor();
            _cursorApplied = true;
        }
    }

    private bool _cursorApplied = false;

    private Color BlendColors(Color baseColor, Color overlay)
    {
        return Color.FromRgba(
            (baseColor.Red * (1 - overlay.Alpha)) + (overlay.Red * overlay.Alpha),
            (baseColor.Green * (1 - overlay.Alpha)) + (overlay.Green * overlay.Alpha),
            (baseColor.Blue * (1 - overlay.Alpha)) + (overlay.Blue * overlay.Alpha),
            Math.Max(baseColor.Alpha, overlay.Alpha)
        );
    }

    private void OnPointerExited(object? sender, PointerEventArgs e)
    {
        if (_associatedView == null) return;
        
        // Restore background color immediately
        if (ApplyHoverHighlight && _originalBackgroundColor != null)
        {
            _associatedView.BackgroundColor = _originalBackgroundColor;
            _originalBackgroundColor = null;
        }
        
        ResetPressEffect();
    }

    private void OnPointerPressed(object? sender, PointerEventArgs e)
    {
        if (_associatedView == null) return;

        uint duration = !MotionPreferences.IsReducedMotionRequested ? 80u : 0u;
        _associatedView.ScaleToAsync(PressedScale, duration, Easing.CubicOut);
        _associatedView.FadeToAsync(PressedOpacity, duration, Easing.CubicOut);
    }

    private void OnPointerReleased(object? sender, PointerEventArgs e)
    {
        ResetPressEffect();
    }

    private void ResetPressEffect()
    {
        if (_associatedView == null) return;

        uint duration = !MotionPreferences.IsReducedMotionRequested ? 150u : 0u;
        _associatedView.ScaleToAsync(1.0, duration, Easing.CubicIn);
        _associatedView.FadeToAsync(1.0, duration, Easing.CubicIn);
    }
}
