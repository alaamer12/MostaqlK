using System.Windows.Input;
using MostaqlK.UI.PlatformComponents;

namespace MostaqlK.UI.DesignSystem;

/// <summary>
/// A shared behavior that adds standard pressing and (on platforms with a pointer/mouse)
/// hovering effects to any View:
/// 1. Scaling (0.98) and Opacity (0.8) change on press/touch-down — this part is genuinely
///    cross-platform (<see cref="PointerGestureRecognizer.PointerPressed"/>/<c>PointerReleased</c>
///    fire for touch input too) and gives touch platforms their native "press" feedback.
/// 2. Hover background highlight and pointer cursor — a desktop/mouse-only concept with no touch
///    equivalent (there is no "hover" without a pointer that can move without touching). This half
///    is implemented per-platform: see <c>PressableEffect.Windows.cs</c> (hover highlight,
///    cross-hover coordination, cursor) and <c>PressableEffect.Android.cs</c> (explicitly
///    a no-op — touch's "native feel" is the shared press/scale feedback above, not a stand-in
///    hover). This mirrors the "native feel per platform, not one platform's design with parts
///    stripped out" rule in cross-platform-ui-conventions.md.
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

    /// <summary>Only meaningful on platforms with a hover concept (Windows); ignored on touch platforms.</summary>
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
        OnHandlerAttachedForPlatform();
    }

    /// <summary>Platform-neutral seam for the hover half of this behavior (Windows: highlight +
    /// cursor + cross-hover coordination; Android/iOS: intentionally no-op, see
    /// <c>PressableEffect.Android.cs</c>/<c>PressableEffect.iOS.cs</c>).</summary>
    partial void OnHandlerAttachedForPlatform();
    partial void HandlePointerEntered();
    partial void HandlePointerExited();
    partial void ApplyPlatformCursor();

    /// <summary>Platform-neutral seam for touch-down feedback shared by the mobile OS family
    /// (Android + iOS export the same <c>_PressableEffect.Mobile.cs</c> haptic tick; Windows has
    /// no implementation since haptics on every mouse click would feel wrong for a desktop app).</summary>
    partial void HandleMobilePressStarted();

    private void OnPointerEntered(object? sender, PointerEventArgs e)
    {
        if (_associatedView == null) return;
        HandlePointerEntered();
    }

    private void OnPointerExited(object? sender, PointerEventArgs e)
    {
        if (_associatedView == null) return;
        HandlePointerExited();
        ResetPressEffect();
    }

    private void OnPointerPressed(object? sender, PointerEventArgs e)
    {
        if (_associatedView == null) return;

        HandleMobilePressStarted();

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
