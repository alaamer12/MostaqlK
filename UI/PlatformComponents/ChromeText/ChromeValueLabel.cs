namespace MostaqlK.UI.PlatformComponents;

/// <summary>
/// The today-counter's numeric value: a <see cref="Label"/> whose glyphs are painted with the
/// mockup's brushed-metal "chrome" sheen (<c>projects.html</c>, <c>.fx-chrome</c> +
/// <c>@keyframes fx-chrome</c> — a 105° brand-ink → <c>color-mix(in srgb, ink 35%, white)</c> →
/// brand-ink band over a 260%-wide pattern, clipped to the text). The public surface stays exactly
/// the <see cref="Label"/> surface call sites already set: <c>Text</c>, <c>AutomationId</c>,
/// <c>FontSize</c>, <c>LineHeight</c>, <c>FontFamily</c>, <c>HorizontalTextAlignment</c> and
/// <c>TextColor</c> (the ink the gradient is built from).
/// </summary>
/// <remarks>
/// <b>Animation safety</b> (`.repertoire/.steering/v3/animation-safety.md`, rules 1–3, and the
/// long-idle postmortem): this unit never calls <c>TranslateToAsync</c>/<c>ScaleToAsync</c>/
/// <c>RotateToAsync</c>/<c>FadeToAsync</c>, never creates a <c>Storyboard</c> and never touches
/// <c>RenderTransform</c> — the crash class both postmortems document is native per-frame
/// transform garbage, and this unit produces none. The Windows half drives ONE reused
/// <c>LinearGradientBrush</c> assigned to the native <c>TextBlock.Foreground</c> and mutates only
/// its two end points per frame (two value-type <c>Point</c> assignments, no managed allocation),
/// advanced by ONE app-wide 33 ms <see cref="IDispatcherTimer"/> shared by every attached label
/// (the <see cref="MostaqlK.UI.DesignSystem.ShimmerBox"/> single-shared-ticker discipline). Each
/// tick parks — mutating nothing — while <see cref="MotionPreferences.IsReducedMotionRequested"/>
/// (which already folds in ambient/tray-hidden suspension) is true, while this label or ANY
/// ancestor is <c>IsVisible = false</c>, or while the handler/native control is gone. Parking
/// leaves the static metal gradient (the specular band centred on the digits), so a parked frame
/// still reads as the mockup's brushed metal rather than as plain text.
/// <para>
/// Cross-platform: the shared half is a plain <see cref="Label"/>; all gradient/ticker code lives
/// in <c>ChromeValueLabel.Windows.cs</c> behind <c>partial</c> seams, per
/// <c>cross-platform-ui-conventions.md</c> (no in-body <c>#if WINDOWS</c>).
/// </para>
/// </remarks>
public partial class ChromeValueLabel : Label
{
    public ChromeValueLabel()
    {
        InitializeChromeAnimationCore();
    }

    /// <summary>
    /// Windows wires the <c>Loaded</c>/<c>Unloaded</c> registration pair here; a no-op on platforms
    /// without a partial, where this unit is intentionally just a plain <see cref="Label"/>.
    /// </summary>
    partial void InitializeChromeAnimationCore();

    /// <summary>
    /// Windows (re)attaches the gradient brush and restores the static parked phase when the
    /// handler is created or replaced. No-op elsewhere.
    /// </summary>
    partial void AttachChromeCore();

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();
        AttachChromeCore();
    }
}
