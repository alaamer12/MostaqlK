using Microsoft.Maui.Controls.Handlers;
using MostaqlK.UI.PlatformComponents;
using MostaqlK.UI.DesignSystem.BrandTokens;
using WinBorder = Microsoft.UI.Xaml.Controls.Border;
using WinGradientStop = Microsoft.UI.Xaml.Media.GradientStop;
using WinLinearGradientBrush = Microsoft.UI.Xaml.Media.LinearGradientBrush;
using WinPoint = Windows.Foundation.Point;

namespace MostaqlK.UI.DesignSystem;

/// <summary>
/// Windows half of <see cref="NewRibbonBadge"/>: the today-counter's brushed-metal "chrome" sheen
/// (<c>projects.html .fx-chrome</c> / <c>@keyframes fx-chrome</c>) painted into the ribbon's brand
/// surface (the native <see cref="WinBorder"/> that MAUI's <see cref="BoxView"/> maps to).
/// </summary>
/// <remarks>
/// <para>
/// <b>Mechanism (identical in spirit to <see cref="PlatformComponents.ChromeValueLabel"/>):</b> ONE
/// reused <see cref="WinLinearGradientBrush"/> is assigned to the native surface's
/// <see cref="WinBorder.Background"/>. The brush keeps five fixed stops (brand, brand, specular, brand,
/// brand at 0 / 40% / 50% / 60% / 100%) and its relative <c>StartPoint</c>/<c>EndPoint</c> span
/// exactly 2.6 ribbon widths (the CSS <c>background-size: 260% 100%</c>). One cycle = sliding that
/// span one full pattern width, so the loop is seamless (the 100% frame is pixel-identical to 0%).
/// The specular band is a gentler mix than the counter's (55% brand + 45% white) so the white "جديد"
/// label stays legible as the sheen passes under it.
/// </para>
/// <para>
/// <b>Crash-safety</b> (animation-safety.md rules 1–3, the long-idle postmortem): the per-frame hot
/// path assigns two value-type <c>Point</c>s on the existing brush and nothing else — no
/// <c>TranslateTo*</c>/<c>ScaleTo*</c>/<c>FadeTo*</c>, no <c>Storyboard</c>, no fresh native object,
/// no <c>RenderTransform</c> churn. The retired background light-band pulse is gone; the sweep is now
/// this gradient, not a moving view.
/// </para>
/// <para>
/// <b>Single shared ticker:</b> ONE static 33 ms <see cref="IDispatcherTimer"/> advances every
/// attached ribbon from wall-clock elapsed time (the <c>ChromeValueLabel</c>/<c>ShimmerBox</c>
/// contract), iterating the registration list backwards under the lock (no <c>ToArray()</c> snapshot).
/// Each tick parks — mutating nothing — under reduced motion (which folds in tray-hidden ambient
/// suspension), while the badge or any ancestor is hidden, or while the handler/native control is
/// gone; a parked frame leaves the static metal band so the ribbon still reads as chrome, not flat.
/// </para>
/// <para>
/// <b>Re-color seam:</b> MAUI's stock <c>BoxView.Color</c> mapping writes a solid
/// <see cref="WinBorder.Background"/>, so an appended mapping (registered once, guarded to the badge's
/// own <see cref="RibbonSurface"/>) re-applies the (re-tinted) gradient in the SAME pass — exactly the
/// ordering trick <c>ChromeValueLabel</c> uses for <c>Label.TextColor</c>. It runs on attach and on
/// every theme flip / source morph, never from the per-frame tick.
/// </para>
/// </remarks>
public partial class NewRibbonBadge
{
    /// <summary>One full cycle — matches the counter's <c>animation: fx-chrome 3.2s ease-in-out infinite</c>.</summary>
    private const double CycleDurationMs = 3200;

    /// <summary>The sheen crosses during the first 55% of the cycle, then the metal holds.</summary>
    private const double SweepDurationMs = CycleDurationMs * 0.55;

    /// <summary>`background-size: 260% 100%` — the gradient pattern is 2.6 ribbon widths wide.</summary>
    private const double PatternPeriod = 2.6;

    private const double InkEdgeStart = 0.40;
    private const double SpecularCenter = 0.50;
    private const double InkEdgeEnd = 0.60;

    /// <summary>55% brand + 45% white specular (softer than the counter's 35% to keep the white label readable).</summary>
    private const float SpecularBrandWeight = 0.55f;

    /// <summary>Relative-Y span of the gradient vector — a shallow diagonal over the wide, short ribbon.</summary>
    private const double GradientTilt = 0.6;

    /// <summary>Phase the sweep parks at: the specular band centred inside the visible 1/2.6 slice.</summary>
    private const double StaticPhase = SpecularCenter - (1.0 / (2.0 * PatternPeriod));

    private static readonly List<NewRibbonBadge> ActiveBadges = [];
    private static IDispatcherTimer? _sharedTicker;
    private static long _startTicks;

    private WinBorder? _surface;
    private WinLinearGradientBrush? _brush;
    private WinGradientStop[]? _stops;
    private WinPoint _startPoint;
    private WinPoint _endPoint;
    private bool _chromeRegistered;
    private bool _parked;

    static NewRibbonBadge()
    {
        // Run AFTER MAUI's built-in Color → solid Background mapping so a theme/palette swap (which
        // re-sets RibbonSurface.Color) can't leave the surface flat: this appended step re-paints the
        // chrome brush in the same mapping pass. Guarded to RibbonSurface so no other BoxView is touched.
        BoxViewHandler.Mapper.AppendToMapping(nameof(BoxView.Color), static (_, view) =>
        {
            if (view is RibbonSurface surface)
            {
                surface.Owner.OnSurfaceColorMapped(surface);
            }
        });
    }

    partial void InitializeChrome()
    {
        Loaded += (_, _) => AttachChrome();
    }

    partial void RegisterChrome()
    {
        if (_chromeRegistered)
        {
            return;
        }

        _chromeRegistered = true;
        lock (ActiveBadges)
        {
            ActiveBadges.Add(this);
        }

        EnsureSharedTickerRunning();
    }

    partial void UnregisterChrome()
    {
        if (!_chromeRegistered)
        {
            return;
        }

        _chromeRegistered = false;
        lock (ActiveBadges)
        {
            ActiveBadges.Remove(this);
        }
    }

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();
        AttachChrome();
    }

    /// <summary>
    /// Re-applies the chrome brush after MAUI wrote a solid Background (initial map or a re-color).
    /// Refreshes the stop colors from the surface's current brand Color, then re-assigns the SAME
    /// brush instance so a live sweep phase survives the recolor.
    /// </summary>
    private void OnSurfaceColorMapped(RibbonSurface surface)
    {
        if (surface.Handler?.PlatformView is not WinBorder border)
        {
            return;
        }

        _surface = border;

        if (_brush is null)
        {
            CreateBrush(surface.Color);
        }
        else
        {
            UpdateChromeColors(surface.Color);
            if (!ReferenceEquals(border.Background, _brush))
            {
                border.Background = _brush;
            }
        }

        ApplyPhase(StaticPhase);
        _parked = true;
    }

    /// <summary>
    /// Attach on handler (re)creation even when no Color change re-ran the mapping. Falls back to the
    /// surface's own Color if the mapping hasn't created the brush yet.
    /// </summary>
    private void AttachChrome()
    {
        if (_background.Handler?.PlatformView is WinBorder border)
        {
            _surface = border;
            if (_brush is null)
            {
                CreateBrush(_background.Color);
            }
            else
            {
                UpdateChromeColors(_background.Color);
                border.Background = _brush;
            }

            ApplyPhase(StaticPhase);
            _parked = true;
        }
        else
        {
            // Handler detached: drop every native reference (no rooted leak).
            _surface = null;
            _brush = null;
            _stops = null;
        }
    }

    private void Tick(double nowMs)
    {
        try
        {
            if (!IsActive || !IsEffectivelyVisible() || _surface is null || _brush is null)
            {
                Park();
                return;
            }

            // Reduced motion folds in IsAmbientMotionSuspended (window hidden to the tray), so the
            // tray-idle case parks too — animation-safety rule 1.
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
            // Purely decorative — a stale/disposed surface must never surface as a crash.
            UnregisterChrome();
        }
    }

    /// <summary>Freeze the sheen at the static metal phase; idempotent (a parked badge mutates nothing).</summary>
    private void Park()
    {
        if (_parked)
        {
            return;
        }

        _parked = true;
        ApplyPhase(StaticPhase);
    }

    /// <summary>Move the fixed-stop gradient by <paramref name="phase"/> periods — the whole hot path.</summary>
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

    /// <summary>Quadratic ease-in-out (the CSS <c>ease-in-out</c> shape) — pure arithmetic, no allocation.</summary>
    private static double EaseInOut(double t)
    {
        t = Math.Clamp(t, 0.0, 1.0);
        return t < 0.5
            ? 2.0 * t * t
            : 1.0 - (Math.Pow((-2.0 * t) + 2.0, 2.0) / 2.0);
    }

    private void CreateBrush(Color brand)
    {
        var brush = new WinLinearGradientBrush
        {
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
        UpdateChromeColors(brand);

        if (_surface is not null)
        {
            _surface.Background = brush;
        }
    }

    /// <summary>Re-apply brand/specular colors. Called on attach and on every recolor — never per frame.</summary>
    private void UpdateChromeColors(Color brand)
    {
        if (_stops is null)
        {
            return;
        }

        var baseInk = brand ?? BrandPalette.Brand;
        var specular = Lighten(baseInk);
        var baseColor = ToWindowsColor(baseInk);
        var specularColor = ToWindowsColor(specular);

        _stops[0].Color = baseColor;
        _stops[1].Color = baseColor;
        _stops[2].Color = specularColor;
        _stops[3].Color = baseColor;
        _stops[4].Color = baseColor;
    }

    /// <summary>`color-mix(in srgb, brand 55%, #ffffff)` per sRGB channel.</summary>
    private static Color Lighten(Color brand) => Color.FromRgba(
        (brand.Red * SpecularBrandWeight) + (1f - SpecularBrandWeight),
        (brand.Green * SpecularBrandWeight) + (1f - SpecularBrandWeight),
        (brand.Blue * SpecularBrandWeight) + (1f - SpecularBrandWeight),
        brand.Alpha);

    private static Windows.UI.Color ToWindowsColor(Color color) => Windows.UI.Color.FromArgb(
        ToByte(color.Alpha),
        ToByte(color.Red),
        ToByte(color.Green),
        ToByte(color.Blue));

    private static byte ToByte(float channel) =>
        (byte)Math.Clamp((int)MathF.Round(channel * 255f), 0, 255);

    /// <summary>True when this badge and every ancestor up to the page are visible (collapsed-parent gate).</summary>
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
        _sharedTicker.Tick += OnSharedTickerTick;
        _sharedTicker.Start();
    }

    private static void OnSharedTickerTick(object? sender, EventArgs e)
    {
        var nowMs = (double)(Environment.TickCount64 - _startTicks);

        // Backwards so a badge that unregisters itself inside Tick (stale catch) never shifts an
        // index out from under the loop; no ToArray() snapshot allocation per frame.
        lock (ActiveBadges)
        {
            for (var i = ActiveBadges.Count - 1; i >= 0; i--)
            {
                ActiveBadges[i].Tick(nowMs);
            }
        }
    }
}
