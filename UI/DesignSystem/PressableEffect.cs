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

    // Whether THIS view's own highlight is currently being suppressed because a nested
    // pressable descendant (e.g. ProjectCard's "عرض في مستقل" chip inside the card) is being
    // hovered/pressed - see SuppressForChildHover/ResumeAfterChildHover below.
    private bool _suppressedByChildHover;

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

        // WinUI's PointerEntered/Exited are geometry-bound to each element's own bounding box:
        // they don't fire on an ancestor just because the pointer moved onto a nested child that's
        // still visually inside the ancestor's rect (e.g. moving from a ProjectCard's body onto its
        // "عرض في مستقل" chip button). So when THIS view's own hover starts, tell the nearest
        // ancestor that also has a PressableEffect to step aside instead of showing both highlights
        // stacked on top of each other.
        FindAncestorPressable()?.SuppressForChildHover();

        if (ApplyHoverHighlight)
        {
            // Always use current color as base for hover, but avoid using a previous hover color
            var currentColor = _associatedView.BackgroundColor ?? Colors.Transparent;
            
            // If we are already hovered, don't re-store original color
            if (_originalBackgroundColor == null)
            {
                _originalBackgroundColor = currentColor;
            }
            
            ApplyHighlightNow();
        }

        // Apply cursor only once
        if (!_cursorApplied)
        {
            ApplyPlatformCursor();
            _cursorApplied = true;
        }
    }

    private void ApplyHighlightNow()
    {
        if (_associatedView == null || _originalBackgroundColor == null)
        {
            return;
        }

        // Modern elegant hover color: slightly lighter in dark, slightly darker in light
        var defaultHover = Application.Current?.RequestedTheme == AppTheme.Dark
            ? Color.FromArgb("#15FFFFFF") // Subtle light overlay for dark theme
            : Color.FromArgb("#0A000000"); // Very subtle dark overlay for light theme

        var highlight = HoverColor ?? defaultHover;

        // If the view has a background, we should blend the highlight
        _associatedView.BackgroundColor = _originalBackgroundColor != Colors.Transparent
            ? BlendColors(_originalBackgroundColor, highlight)
            : highlight;
    }

    /// <summary>
    /// Called by a nested descendant's <see cref="PressableEffect"/> when IT starts hovering, so
    /// this (ancestor) view's own highlight doesn't stay stuck showing underneath/behind the
    /// child's own highlight for as long as the pointer sits anywhere within this view's bounds.
    /// </summary>
    internal void SuppressForChildHover()
    {
        if (_associatedView == null || _originalBackgroundColor == null)
        {
            return;
        }

        _suppressedByChildHover = true;
        _associatedView.BackgroundColor = _originalBackgroundColor;
    }

    /// <summary>
    /// Called by a nested descendant's <see cref="PressableEffect"/> when IT stops hovering
    /// (pointer exited the child but is still within this ancestor's bounds), so this view's own
    /// highlight resumes as if the pointer had just re-entered it.
    /// </summary>
    internal void ResumeAfterChildHover()
    {
        if (!_suppressedByChildHover)
        {
            return;
        }

        _suppressedByChildHover = false;
        if (ApplyHoverHighlight)
        {
            ApplyHighlightNow();
        }
    }

    /// <summary>Walks up the visual tree to find the nearest ancestor carrying its own <see cref="PressableEffect"/> (e.g. a ProjectCard's AppCard wrapping this chip button).</summary>
    private PressableEffect? FindAncestorPressable()
    {
        var parent = (_associatedView as Element)?.Parent;
        while (parent != null)
        {
            if (parent is View view)
            {
                foreach (var behavior in view.Behaviors)
                {
                    if (behavior is PressableEffect ancestorEffect && ancestorEffect != this)
                    {
                        return ancestorEffect;
                    }
                }
            }
            parent = parent.Parent;
        }
        return null;
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

        // Pointer left this (child) view but is still within the ancestor's bounds (that's exactly
        // why WinUI didn't already re-trigger the ancestor's own PointerEntered) - let the
        // ancestor's highlight take back over.
        FindAncestorPressable()?.ResumeAfterChildHover();

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
