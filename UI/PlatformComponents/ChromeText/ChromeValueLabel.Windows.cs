using Microsoft.Maui.Handlers;
using Microsoft.UI.Xaml.Controls;
using MostaqlK.UI.DesignSystem.BrandTokens;
using WinGradientStop = Microsoft.UI.Xaml.Media.GradientStop;
using WinLinearGradientBrush = Microsoft.UI.Xaml.Media.LinearGradientBrush;
using WinPoint = Windows.Foundation.Point;

namespace MostaqlK.UI.PlatformComponents;

/// <summary>
/// Windows half of <see cref="ChromeValueLabel"/>: the mockup's animated brushed-metal sheen
/// (<c>projects.html</c>, <c>.fx-chrome</c> / <c>@keyframes fx-chrome</c>) painted into the native
/// <see cref="TextBlock"/>'s <c>Foreground</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Mechanism (fixed):</b> ONE reused <see cref="WinLinearGradientBrush"/> is assigned to the
/// label's <see cref="TextBlock.Foreground"/> — WinUI fills the glyphs with any brush, so the CSS
/// <c>background-clip: text</c> effect needs no per-glyph work. The brush keeps five fixed stops
/// (ink, ink, specular, ink, ink = the mockup's <c>0 40% / 50% / 60% 100%</c>) and its relative
/// <c>StartPoint</c>/<c>EndPoint</c> span exactly 2.6 label widths (the CSS
/// <c>background-size: 260% 100%</c>). One animation period = sliding that span one full pattern
/// width (0 → 2.6 in relative X), which is why the loop has no seam: the 100% frame is
/// pixel-identical to the 0% frame.
/// </para>
/// <para>
/// <b>Crash-safety (the binding reason this is shaped this way):</b> the long-idle postmortem's
/// fail-fast came from MAUI allocating a fresh native <c>CompositeTransform</c>/<c>ScaleTransform</c>
/// for every transform-animation frame and the CLR finalizer later releasing them off the UI
/// thread. This tick path allocates no native objects at all: it assigns two value-type
/// <c>Point</c>s and nothing else. No <c>TranslateTo*</c>/<c>ScaleTo*</c>/<c>RotateTo*</c>/
/// <c>FadeTo*</c>, no <c>Storyboard</c>, no <c>RenderTransform</c> churn — see
/// <c>.repertoire/.steering/v3/animation-safety.md</c> rules 1–3.
/// </para>
/// <para>
/// <b>Single shared ticker:</b> ONE static 33 ms <see cref="IDispatcherTimer"/> advances every
/// attached label from wall-clock elapsed time (the <c>ShimmerBox</c> contract), so N labels still
/// mean one driver. The ticker iterates the registration list backwards under the same lock instead
/// of snapshotting with <c>ToArray()</c>: a label that unregisters itself from inside
/// <see cref="Tick"/> (the stale-handler catch) cannot shift an index out from under the loop, and
/// the hot path stays allocation-free.
/// </para>
/// <para>
/// <b>Parking:</b> each tick mutates nothing while
/// <see cref="MotionPreferences.IsReducedMotionRequested"/> (folds in tray-hidden ambient
/// suspension), while this label or any ancestor is <c>IsVisible = false</c> (a collapsed parent
/// keeps loaded children alive, exactly the amplifier ShimmerBox documents), or while the handler /
/// native control is gone. The first parked tick leaves the static metal gradient (specular band
/// centred on the digits) and later ticks do nothing at all until the conditions clear.
/// </para>
/// </remarks>
public partial class ChromeValueLabel
{
    /// <summary>One full cycle — the mockup's <c>animation: fx-chrome 3.2s ease-in-out infinite</c>.</summary>
    private const double CycleDurationMs = 3200;

    /// <summary>The sheen crosses during the first 55% of the cycle, then the metal holds (`@keyframes fx-chrome`).</summary>
    private const double SweepDurationMs = CycleDurationMs * 0.55;

    /// <summary>`background-size: 260% 100%` — the gradient pattern is 2.6 label widths wide.</summary>
    private const double PatternPeriod = 2.6;

    /// <summary>`var(--ink) 0 40%` — the leading ink plateau.</summary>
    private const double InkEdgeStart = 0.40;

    /// <summary>`color-mix(in srgb, var(--ink) 35%, #ffffff) 50%` — the specular band's centre.</summary>
    private const double SpecularCenter = 0.50;

    /// <summary>`var(--ink) 60% 100%` — the trailing ink plateau.</summary>
    private const double InkEdgeEnd = 0.60;

    /// <summary>The mockup's 35% ink / 65% white specular mix (sRGB component mix, like `color-mix`).</summary>
    private const float SpecularInkWeight = 0.35f;

    /// <summary>
    /// Relative-Y span of the gradient vector. The CSS line is 105° over a 2.6-wide tile, i.e. about
    /// 15° below horizontal in pixels; on a short 30px value label this span reproduces that shallow
    /// diagonal without depending on the label's live width (X already spans 2.6 widths).
    /// </summary>
    private const double GradientTilt = 0.6;

    /// <summary>
    /// Phase the animation parks at: the specular band centred inside the visible 1/2.6-wide slice
    /// of the pattern. The mockup's reduced-motion frame sits at position 0 (solid ink); the brief
    /// requires the static state to keep the metal band, so parked text still reads as brushed metal.
    /// </summary>
    private const double StaticPhase = SpecularCenter - (1.0 / (2.0 * PatternPeriod));

    /// <summary>All attached labels, ticked by the single <see cref="_sharedTicker"/>.</summary>
    private static readonly List<ChromeValueLabel> ActiveLabels = [];
    private static IDispatcherTimer? _sharedTicker;
    private static long _startTicks;

    private TextBlock? _textBlock;
    private WinLinearGradientBrush? _brush;
    private WinGradientStop[]? _stops;
    private WinPoint _startPoint;
    private WinPoint _endPoint;
    private bool _registered;
    private bool _parked;

    static ChromeValueLabel()
    {
        // Run AFTER MAUI's built-in TextColor → solid Foreground mapping. A brand-palette or theme
        // swap re-resolves `{DynamicResource BrandInk}`, MAUI's stock mapper then replaces the
        // gradient with a flat colour, and this appended step puts the (re-coloured) chrome brush
        // back in the same mapping pass. `ModifyMapping` would replace the stock mapping instead of
        // following it, and a custom key would never run — the append is what guarantees ordering
        // (same handler-mapper pattern as AppEntry's Windows tweak).
        LabelHandler.Mapper.AppendToMapping(nameof(Label.TextColor), static (_, view) =>
        {
            if (view is ChromeValueLabel label)
            {
                label.OnTextColorMappedCore();
            }
        });
    }

    partial void InitializeChromeAnimationCore()
    {
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    partial void AttachChromeCore()
    {
        if (Handler?.PlatformView is not TextBlock textBlock)
        {
            // Detached handler: drop every native reference (no static or self-rooted leak).
            _textBlock = null;
            _brush = null;
            _stops = null;
            return;
        }

        _textBlock = textBlock;

        if (_brush is null)
        {
            CreateBrush();
        }
        else
        {
            UpdateInkColors();
            if (!ReferenceEquals(textBlock.Foreground, _brush))
            {
                textBlock.Foreground = _brush;
            }
        }

        // Rest state until the shared ticker animates: the static metal band, and "already parked"
        // so a reduced-motion tick performs no mutation at all.
        ApplyPhase(StaticPhase);
        _parked = true;
    }

    /// <summary>
    /// Re-applies the ink/specular colours and restores the gradient brush after MAUI's stock
    /// <c>TextColor</c> mapper wrote a solid <c>Foreground</c>. Runs only on ink changes (palette or
    /// theme swap) — never from the per-frame tick.
    /// </summary>
    private void OnTextColorMappedCore()
    {
        if (_brush is null || _textBlock is null)
        {
            return;
        }

        UpdateInkColors();

        // Same brush instance, so the live animation phase survives the recolor.
        _textBlock.Foreground = _brush;
    }

    private void OnLoaded(object? sender, EventArgs e) => Register();

    private void OnUnloaded(object? sender, EventArgs e) => Unregister();

    private void Register()
    {
        if (_registered)
        {
            return;
        }

        _registered = true;
        lock (ActiveLabels)
        {
            ActiveLabels.Add(this);
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
        lock (ActiveLabels)
        {
            ActiveLabels.Remove(this);
        }
    }

    /// <summary>
    /// Advances this label by one shared-ticker frame. Never throws outward: the label can be
    /// detached/recycled between one tick and the next, so a stale reference hitting a torn-down
    /// handler must be tolerated rather than propagate (same contract as <c>ShimmerBox.Tick</c>).
    /// </summary>
    private void Tick(double nowMs)
    {
        try
        {
            if (!IsEffectivelyVisible() || Handler is null || _textBlock is null || _brush is null)
            {
                Park();
                return;
            }

            // Reduced motion folds in IsAmbientMotionSuspended (window hidden to the tray), so the
            // tray-idle case parks here too — nobody can see an invisible label, and every frame
            // would still drive native render work. Rule 1 of animation-safety.md: check on every
            // iteration and park at the static state.
            if (MotionPreferences.IsReducedMotionRequested)
            {
                Park();
                return;
            }

            _parked = false;

            var elapsedInCycle = nowMs % CycleDurationMs;
            if (elapsedInCycle < 0)
            {
                elapsedInCycle += CycleDurationMs;
            }

            // First 55%: an eased crossing; the rest: the metal holds (the mockup's flat tail).
            var phase = elapsedInCycle >= SweepDurationMs
                ? 1.0
                : EaseInOut(elapsedInCycle / SweepDurationMs);

            ApplyPhase(phase);
        }
        catch (Exception)
        {
            // Purely decorative — a stale/disposed handler must never surface as a crash.
            Unregister();
        }
    }

    /// <summary>
    /// Freezes the sheen at the static metal phase. Idempotent: once parked, subsequent ticks stop
    /// before touching the brush, so a parked label performs zero mutations and zero allocations.
    /// </summary>
    private void Park()
    {
        if (_parked)
        {
            return;
        }

        _parked = true;
        ApplyPhase(StaticPhase);
    }

    /// <summary>
    /// Moves the (fixed-stop) gradient pattern by <paramref name="phase"/> periods. Only the two
    /// value-type end points change — the brush instance, its stops and its colours are untouched,
    /// so this is the entire per-frame hot path.
    /// </summary>
    private void ApplyPhase(double phase)
    {
        if (_brush is null)
        {
            return;
        }

        var patternStart = -PatternPeriod * phase;

        _startPoint.X = patternStart;
        _startPoint.Y = 0.5 - (GradientTilt / 2.0);
        _endPoint.X = patternStart + PatternPeriod;
        _endPoint.Y = 0.5 + (GradientTilt / 2.0);

        _brush.StartPoint = _startPoint;
        _brush.EndPoint = _endPoint;
    }

    /// <summary>
    /// Quadratic ease-in-out (the CSS <c>ease-in-out</c> shape). Pure arithmetic — no allocation.
    /// </summary>
    private static double EaseInOut(double t)
    {
        t = Math.Clamp(t, 0.0, 1.0);
        return t < 0.5
            ? 2.0 * t * t
            : 1.0 - (Math.Pow((-2.0 * t) + 2.0, 2.0) / 2.0);
    }

    /// <summary>
    /// Creates the single brush: five fixed stops sampled from the mockup's stripe
    /// (ink 0–40%, specular at 50%, ink 60–100%). The mapping mode is WinUI's default
    /// <c>RelativeToBoundingBox</c> (set explicitly for clarity), which is what makes
    /// <see cref="PatternPeriod"/> mean "2.6 label widths".
    /// </summary>
    private void CreateBrush()
    {
        var brush = new WinLinearGradientBrush
        {
            // Explicit (also WinUI's default) so the 2.6-unit span below can never be read as pixels.
            MappingMode = Microsoft.UI.Xaml.Media.BrushMappingMode.RelativeToBoundingBox,
        };

        _stops =
        [
            new WinGradientStop { Offset = 0.0 },
            new WinGradientStop { Offset = InkEdgeStart },
            new WinGradientStop { Offset = SpecularCenter },
            new WinGradientStop { Offset = InkEdgeEnd },
            new WinGradientStop { Offset = 1.0 },
        ];

        foreach (var stop in _stops)
        {
            brush.GradientStops.Add(stop);
        }

        _brush = brush;
        UpdateInkColors();

        if (_textBlock is not null)
        {
            _textBlock.Foreground = brush;
        }
    }

    /// <summary>
    /// Re-applies the ink/specular colours. Called on attach and on every ink change (brand-palette
    /// swap or theme flip) — never from the per-frame tick.
    /// </summary>
    private void UpdateInkColors()
    {
        if (_stops is null)
        {
            return;
        }

        var ink = TextColor ?? BrandPalette.BrandInk;
        var specular = Lighten(ink);
        var inkColor = ToWindowsColor(ink);
        var specularColor = ToWindowsColor(specular);

        _stops[0].Color = inkColor;
        _stops[1].Color = inkColor;
        _stops[2].Color = specularColor;
        _stops[3].Color = inkColor;
        _stops[4].Color = inkColor;
    }

    /// <summary>`color-mix(in srgb, var(--ink) 35%, #ffffff)` — 35% ink plus 65% white per sRGB channel.</summary>
    private static Color Lighten(Color ink) => Color.FromRgba(
        (ink.Red * SpecularInkWeight) + (1f - SpecularInkWeight),
        (ink.Green * SpecularInkWeight) + (1f - SpecularInkWeight),
        (ink.Blue * SpecularInkWeight) + (1f - SpecularInkWeight),
        ink.Alpha);

    private static Windows.UI.Color ToWindowsColor(Color color) => Windows.UI.Color.FromArgb(
        ToByte(color.Alpha),
        ToByte(color.Red),
        ToByte(color.Green),
        ToByte(color.Blue));

    private static byte ToByte(float channel) =>
        (byte)Math.Clamp((int)MathF.Round(channel * 255f), 0, 255);

    /// <summary>
    /// True when this label and every ancestor up to the page are visible. MAUI maps
    /// <c>IsVisible=false</c> to a collapsed platform element that KEEPS its children attached
    /// (their own <c>IsVisible</c> stays true and <c>Loaded</c> already fired), so a per-instance
    /// gate on this label's own flag alone would leave hidden ancestors ticking.
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
        _sharedTicker.Interval = TimeSpan.FromMilliseconds(33); // ~30fps — smooth enough for a slow sheen.
        _sharedTicker.Tick += (_, _) => TickerGuard.Run(_sharedTicker, "ChromeValueLabel", OnSharedTickerTick);
        _sharedTicker.Start();
    }

    private static void OnSharedTickerTick()
    {
        var nowMs = (double)(Environment.TickCount64 - _startTicks);

        // Backwards so a label that unregisters itself inside Tick (stale-handler catch) never
        // shifts an index out from under this loop; no ToArray() snapshot allocation per frame.
        lock (ActiveLabels)
        {
            for (var i = ActiveLabels.Count - 1; i >= 0; i--)
            {
                ActiveLabels[i].Tick(nowMs);
            }
        }
    }
}
