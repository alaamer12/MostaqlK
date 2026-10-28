using System.Numerics;
using System.Runtime.ExceptionServices;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media.Imaging;
using MostaqlK.UI.DesignSystem.BrandTokens;
using WinUIColor = Windows.UI.Color;
using WinUICanvas = Microsoft.UI.Xaml.Controls.Canvas;
using WinUIFrameworkElement = Microsoft.UI.Xaml.FrameworkElement;
using WinUIImage = Microsoft.UI.Xaml.Controls.Image;
using WinUIPanel = Microsoft.UI.Xaml.Controls.Panel;
using WinUIRect = Windows.Foundation.Rect;
using WinUISize = Windows.Foundation.Size;
using WinUIStretch = Microsoft.UI.Xaml.Media.Stretch;
using WinUIUIElement = Microsoft.UI.Xaml.UIElement;
using WinUIVisual = Microsoft.UI.Composition.Visual;
using WinUIWindow = Microsoft.UI.Xaml.Window;

namespace MostaqlK.UI.PlatformComponents;

/// <summary>
/// Windows half of <see cref="SourceMorphTransition"/>: the TRUE radial reveal from the mockup,
/// built entirely from render-thread compositor property animations. One
/// <see cref="RenderTargetBitmap"/> snapshot of the OLD frame is inserted (through the runtime
/// window-host overlay resolved here — never via a <c>MainWindowPage</c>/<c>App.xaml.cs</c> edit)
/// BELOW the live content; the caller's mutation then lands instantly, making the live surface the
/// NEW state; the live visual is clipped by a <see cref="CompositionGeometricClip"/> whose
/// <see cref="CompositionEllipseGeometry"/>.<c>Radius</c> (a <c>Vector2</c> of semi-axes in
/// WinUI 3, and a registered animatable d-property — <c>StartAnimation("Radius", …)</c> drives it
/// on the render thread) grows from 2% to the farthest-corner distance around the clicked origin
/// via a <see cref="Compositor.CreateVector2KeyFrameAnimation"/> (ease-in-out), while a
/// <see cref="ShapeVisual"/> orbit ring (3px brand stroke + a wider 45%-alpha glow stroke, each
/// sprite shape riding its own ellipse geometry on the same "Radius" channel, whole visual
/// opacity fading over the last 25% per contract §4.3) rides above everything and is torn
/// down at the end.
/// <para>
/// This disproves the dev25 deferral claim: <c>Radius</c> is a registered d-property of the
/// geometry/shape composition objects, so <c>CompositionObject.StartAnimation("Radius", …)</c>
/// animates it on the render thread exactly like <c>Visual.Opacity</c> or a shape's <c>Size</c>
/// (Microsoft's own composition samples animate geometry d-properties this way). NOTHING here
/// allocates a managed transform per frame — no <c>CompositeTransform</c>, no XAML
/// <c>RenderTransform</c>, no <c>DispatcherTimer</c>-driven property loop — which is the class
/// <c>.repertoire/.steering/v3/animation-safety.md</c> bans; the whole reveal is four finite
/// compositor key-frame animations + one one-shot finalizer (rules 2 + 3).
/// </para>
/// <para>
/// <b>No-unclipped-frame / no-breaking-glass:</b> the clip is applied (base radius = the small
/// start radius) and the OLD-frame snapshot layer inserted BEFORE the mutation runs, all inside
/// one synchronous UI-thread block, so the compositor never renders even one frame of the new
/// state unclipped, and never one frame of bare backdrop — the layer directly beneath the clipped
/// surface is a pixel-identical copy of what that surface drew one moment earlier.
/// </para>
/// <para>
/// <b>Degrade ladder:</b> capture failure / unexpected window tree ⇒ the dev25 compositor opacity
/// cross-dissolve (rule 3's sanctioned fallback, still zero transforms); no dispatcher timer ⇒
/// plain instant swap. Every rung applies the mutation exactly once and leaks no layer, clip, or
/// compositor animation — <see cref="Reveal.Finish"/> is the single idempotent landing point used
/// by the completion tick, the re-entrancy fast-finish, and every catch path.
/// </summary>
public static partial class SourceMorphTransition
{
    // The opacity the surface dips to at the dissolve's midpoint on the DEGRADED fallback path.
    // Kept high (never near 0) so the page's opaque background never fully shows through.
    private const float DipOpacity = 0.55f;

    // Compositor d-property names animated here — all on composition objects only (gate 3).
    private const string OpacityPropertyName = "Opacity";
    private const string RadiusPropertyName = "Radius";

    // Small grace so the single-shot cleanup tick lands just after the render-thread animations end.
    private const double CompletionGraceMs = 60d;

    // Mockup values (§4.3 / projects.html): the reveal + ring grow from 0.02 of the full radius,
    // the ring is a 3px stroke with an outer glow at `color-mix(brand 45%)`, and it fades over the
    // last 25% of the duration. Stroke widths are design DIPs, scaled by the raster scale below.
    private const float RevealStartFactor = 0.02f;
    private const float RingStrokeDips = 3f;
    private const float RingGlowStrokeDips = 10f;
    private const float RingGlowAlpha = 0.45f;
    private const float RingFadeStartNormalized = 0.75f;

    // The reveal currently in flight (re-entrancy fast-finish target; null when idle). All
    // compositor/overlay objects are owned by it and released through its single Finish().
    private static Reveal? _current;

    // Bumped per reveal AND per fast-finish so a superseded reveal's suspended capture
    // continuation recognizes it is stale and lands quietly instead of starting a second effect.
    private static int _revealToken;

    // One reused single-shot timer finalizing whichever reveal/dissolve is currently running —
    // stopped + re-armed per reveal, stopped outright on a fast-finish. NOT a per-frame loop:
    // it ticks exactly once, after the render-thread animations have landed (animation-safety 1).
    private static IDispatcherTimer? _cleanupTimer;

    // ------------------------------------------------------------------ quiesced pre-capture (dev30 click path)
    //
    // A click must START the reveal inside its own frame; a capture begun at click costs the
    // RenderAsync round-trip before anything is on screen. So one cached RenderTargetBitmap of the
    // morph target's anchor region is refreshed ~1 frame after each settle — event-driven ONLY:
    // reveal finish, brand/theme repaint (BrandPalette.PaletteChanged, which also covers the source
    // morph's own swap), and anchor resize. Cache miss (cold start, invalidated underneath) falls
    // back to the existing capture-then-reveal; the full degrade ladder is untouched and the
    // Run(...) API is frozen. Idle app ⇒ no armed timer, no recurring capture (heartbeat-safe).

    // ~1 dispatcher frame + paint settle margin after which the refreshed frame is grabbed.
    private const double QuiesceRefreshDelayMs = 150d;

    private sealed record CachedFrame(
        WinUIFrameworkElement Anchor,
        double Scale,
        double WidthDip,
        double HeightDip,
        RenderTargetBitmap Snapshot);

    private static CachedFrame? _cachedFrame;
    private static IDispatcherTimer? _refreshTimer;
    private static bool _refreshPending;

    static SourceMorphTransition()
    {
        // App-lifetime static publisher ⇒ app-lifetime static subscriber (one handler, no instance
        // roots). A palette repaint (theme flip or morph) makes the cached OLD frame wrong-colored:
        // drop it and re-quiesce-capture the new one shortly after.
        BrandPalette.PaletteChanged += OnPaletteInvalidated;
    }

    static partial void PerformReveal(
        VisualElement? origin,
        VisualElement targetRegion,
        Action applyStateChange,
        uint durationMs,
        ref bool handled)
    {
        if (targetRegion.Handler?.PlatformView is not WinUIUIElement element)
        {
            // Handler not realized yet — nothing to animate. Leave handled=false; Run applies instantly.
            return;
        }

        // Take ownership of the mutation NOW: Reveal guarantees applyStateChange runs EXACTLY
        // ONCE from here on (at the start of its effect, or instantly on any failure rung), even
        // when this method is superseded mid-capture or the visual tree falls apart.
        handled = true;

        var reveal = new Reveal(element, targetRegion, origin, applyStateChange, durationMs, ++_revealToken);
        _current = reveal;
        _running = true;
        reveal.Begin();
    }

    static partial void FastFinishCore()
    {
        // Land the in-flight reveal NOW: stops its compositor animations, removes the overlay
        // layers, restores the unclipped surface, and — when a rapid second click superseded a
        // reveal that had not swapped yet — applies its pending mutation synchronously so the
        // superseded intent is never lost (and the new capture snapshots the correct old frame).
        _cleanupTimer?.Stop();
        _revealToken++;
        _current?.Finish();
        CompleteReveal();
        ScheduleQuiescedRefresh();
    }

    // One reused single-shot timer whose tick finalizes whatever reveal is currently running.
    private static IDispatcherTimer? EnsureCleanupTimer()
    {
        if (_cleanupTimer is not null)
        {
            return _cleanupTimer;
        }

        var timer = Application.Current?.Dispatcher.CreateTimer();
        if (timer is null)
        {
            return null;
        }

        timer.Tick += (_, _) => TickerGuard.Run(timer, "SourceMorphCleanup", OnCleanupTick);
        _cleanupTimer = timer;
        return timer;
    }

    private static void OnCleanupTick()
    {
        // MAUI's IDispatcherTimer repeats until stopped; stopping here makes each reveal's finalize
        // a single one-shot (re-armed via Stop()+Start() on the next reveal).
        _cleanupTimer?.Stop();
        _current?.Finish();
        CompleteReveal();
        ScheduleQuiescedRefresh();
    }

    private static void ArmCleanup(uint durationMs)
    {
        if (_cleanupTimer is not { } timer)
        {
            return;
        }

        timer.Stop();
        timer.Interval = TimeSpan.FromMilliseconds(durationMs + CompletionGraceMs);
        timer.Start();
    }

    // ------------------------------------------------------------------ pre-capture cache ops

    /// <summary>
    /// Queue the one-shot quiesce re-capture. Called from every reveal landing, the palette/theme
    /// invalidation, and anchor resize — NEVER on a standing schedule: after the timer ticks once
    /// and the frame is cached, nothing stays armed until the next invalidating event.
    /// </summary>
    private static void ScheduleQuiescedRefresh()
    {
        _refreshPending = true;
        if (EnsureRefreshTimer() is not { } timer)
        {
            _refreshPending = false;
            return;
        }

        timer.Stop();
        timer.Interval = TimeSpan.FromMilliseconds(QuiesceRefreshDelayMs);
        timer.Start();
    }

    private static IDispatcherTimer? EnsureRefreshTimer()
    {
        if (_refreshTimer is not null)
        {
            return _refreshTimer;
        }

        var timer = Application.Current?.Dispatcher.CreateTimer();
        if (timer is null)
        {
            return null;
        }

        timer.Tick += (_, _) => TickerGuard.Run(timer, "SourceMorphRefresh", OnRefreshTick);
        _refreshTimer = timer;
        return timer;
    }

    private static void OnRefreshTick()
    {
        // One-shot semantics, same convention as the cleanup tick: stop first, then decide.
        _refreshTimer?.Stop();

        if (!_refreshPending)
        {
            return;
        }

        if (_running)
        {
            // A fresh reveal started right after the previous settle — capturing now would snapshot
            // a clipped/ringed surface. Re-arm once; bounded by the in-flight reveal's duration,
            // and repeated spam simply keeps deferring (still one one-shot timer, never per-frame).
            if (_refreshTimer is { } timer)
            {
                timer.Interval = TimeSpan.FromMilliseconds(QuiesceRefreshDelayMs);
                timer.Start();
            }

            return;
        }

        _ = CaptureForCacheAsync();
    }

    private static async Task CaptureForCacheAsync()
    {
        _refreshPending = false;

        try
        {
            // Hidden-to-tray: nobody will click a hidden window's rail — skip the capture entirely
            // (the next reveal finish / palette event re-queues it once the window is back).
            if (MotionPreferences.IsAmbientMotionSuspended)
            {
                return;
            }

            // The rail + theme paths always morph the default target (the window's root page); the
            // cache keys on the anchor identity/size, so a caller with an explicit targetRegion
            // simply misses the cache and pays today's capture — never a wrong frame.
            if (ResolveDefaultTarget() is not VisualElement mauiTarget
                || mauiTarget.Handler?.PlatformView is not WinUIUIElement element)
            {
                InvalidateCache();
                return;
            }

            if (!TryResolveAnchor(element, mauiTarget, out var panel, out var anchor, out var scale)
                || panel is null || anchor is null)
            {
                InvalidateCache();
                return;
            }

            var snapshot = await CaptureRegionAsync(anchor, scale);

            // A reveal slipped in while we were capturing, or the frame came back degenerate:
            // drop it; the reveal's own landing re-queues the refresh.
            if (snapshot is null || _running)
            {
                return;
            }

            SetCache(new CachedFrame(anchor, scale, anchor.ActualWidth, anchor.ActualHeight, snapshot));
        }
        catch (Exception)
        {
            // A failed background refresh must never surface; the ladder just captures at click.
            InvalidateCache();
        }
    }

    /// <summary>Consume the cached OLD frame for this reveal, or null (cold/invalidated/mismatch).
    /// Consumption invalidates: the frame is now painted as the overlay and a fresh one is
    /// re-queued by the reveal's landing.</summary>
    private static RenderTargetBitmap? TryConsumeCache(WinUIFrameworkElement? anchor, double scale)
    {
        var cached = _cachedFrame;
        if (cached is null || anchor is null)
        {
            return null;
        }

        if (!ReferenceEquals(cached.Anchor, anchor)
            || Math.Abs(cached.Scale - scale) > 0.01
            || Math.Abs(cached.WidthDip - anchor.ActualWidth) > 0.5
            || Math.Abs(cached.HeightDip - anchor.ActualHeight) > 0.5)
        {
            InvalidateCache();
            return null;
        }

        var snapshot = cached.Snapshot;
        InvalidateCache();
        return snapshot;
    }

    private static void SetCache(CachedFrame frame)
    {
        InvalidateCache();
        _cachedFrame = frame;
        frame.Anchor.SizeChanged += OnAnchorSizeChanged;
    }

    private static void InvalidateCache()
    {
        if (_cachedFrame is { } cached)
        {
            cached.Anchor.SizeChanged -= OnAnchorSizeChanged;
        }

        _cachedFrame = null;
    }

    private static void OnAnchorSizeChanged(object? sender, Microsoft.UI.Xaml.SizeChangedEventArgs e)
    {
        // Resize invalidates the cached region geometry AND pixels — re-quiesce and re-capture.
        InvalidateCache();
        ScheduleQuiescedRefresh();
    }

    private static void OnPaletteInvalidated()
    {
        // Theme flip or brand morph (both land as a PaletteChanged): the cached frame carries the
        // OLD palette. Drop it now (a click in the meantime degrades to capture-then-reveal — the
        // pre-existing, still-correct rung) and refresh the new frame shortly after.
        InvalidateCache();
        ScheduleQuiescedRefresh();
    }

    /// <summary>
    /// Shared anchor resolution (window-content panel + the panel's direct child containing the
    /// target's platform view), used by a live reveal AND by the quiesce refresh so both key on
    /// the exact same region. Pure reads; any tree surprise ⇒ false, never a throw.
    /// </summary>
    private static bool TryResolveAnchor(
        WinUIUIElement platformElement,
        VisualElement targetRegion,
        out WinUIPanel? panel,
        out WinUIFrameworkElement? anchor,
        out double scale)
    {
        panel = null;
        anchor = null;
        scale = 0d;

        try
        {
            if (ResolveMauiWindowFor(targetRegion)?.Handler?.PlatformView is not WinUIWindow nativeWindow
                || nativeWindow.Content is not WinUIPanel contentPanel)
            {
                return false;
            }

            if (platformElement is not WinUIFrameworkElement elementFe)
            {
                return false;
            }

            var current = elementFe;
            var guard = 0;
            while (!ReferenceEquals(current.Parent, contentPanel))
            {
                if (current.Parent is not WinUIFrameworkElement parent || ++guard > 64)
                {
                    return false;
                }

                current = parent;
            }

            if (ReferenceEquals(current, contentPanel) || contentPanel.Children.IndexOf(current) < 0)
            {
                return false;
            }

            if (contentPanel.XamlRoot is { } xamlRoot)
            {
                scale = xamlRoot.RasterizationScale;
            }
            else
            {
                return false;
            }

            panel = contentPanel;
            anchor = current;
            return scale > 0d && current.ActualWidth > 1d && current.ActualHeight > 1d;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static Microsoft.Maui.Controls.Window? ResolveMauiWindowFor(VisualElement targetRegion)
    {
        var windows = Application.Current?.Windows;
        if (windows is null)
        {
            return null;
        }

        foreach (var window in windows)
        {
            if (ReferenceEquals(window.Page, targetRegion))
            {
                return window;
            }
        }

        foreach (var window in windows)
        {
            if (window.Page is not null)
            {
                return window;
            }
        }

        return null;
    }

    private static async Task<RenderTargetBitmap?> CaptureRegionAsync(WinUIFrameworkElement anchor, double scale)
    {
        try
        {
            var rtb = new RenderTargetBitmap();

            // WinUI3's RTB exposes no settable DpiX/DpiY — pin the resolution by rendering into the
            // RAW-PIXEL size of the anchored region (compositor space = physical pixels), so at 150%
            // scale the snapshot is 1:1 sharp against what the surface just drew.
            var pixelSize = new WinUISize(
                Math.Round(anchor.ActualWidth * scale),
                Math.Round(anchor.ActualHeight * scale));
            await rtb.RenderAsync(anchor, (int)pixelSize.Width, (int)pixelSize.Height);
            return rtb.PixelWidth < 2 || rtb.PixelHeight < 2 ? null : rtb;
        }
        catch (Exception)
        {
            // MAUI/WinUI capture surprise — degrade to the dissolve instead of failing the morph.
            return null;
        }
    }

    /// <summary>
    /// One reveal attempt: owns the capture→build→animate pipeline, the overlay layers, the
    /// compositor objects, and the single idempotent <see cref="Finish"/> that lands them all.
    /// Instances are short-lived (created on a click, finished by tick / fast-finish / failure).
    /// </summary>
    private sealed class Reveal
    {
        private readonly WinUIUIElement _element;       // the target region's platform view
        private readonly VisualElement _targetRegion;   // used to find the hosting MAUI window
        private readonly VisualElement? _origin;        // clicked tile / theme button (ring center)
        private readonly Action _applyStateChange;
        private readonly uint _durationMs;
        private readonly int _token;

        private bool _applied;
        private bool _finished;

        // Host resolution (runtime, per invocation — never values baked at load).
        private WinUIPanel? _panel;                     // the window-content panel hosting overlays
        private WinUIFrameworkElement? _anchor;         // the panel's DIRECT child containing the target
        private double _scale = 1d;                     // RasterizationScale: DIP → compositor raw pixels

        // XAML overlay layers + their composition peers (all nulled after teardown).
        private WinUICanvas? _snapshotLayer;
        private WinUICanvas? _ringLayer;
        private WinUIFrameworkElement? _ringHost;
        private WinUIVisual? _clippedVisual;            // the live surface the radial clip rode
        private CompositionClip? _clip;
        private WinUIVisual? _dipVisual;                // degraded-path dissolve target

        // (object, d-property) pairs holding a started animation — every entry gets StopAnimation.
        private readonly List<(CompositionObject Target, string Property)> _started = [];

        // Every composition object created by this reveal — every entry gets Dispose() on the UI
        // thread so nothing ever falls to the CLR finalizer thread (the postmortem's fail-fast).
        private readonly List<CompositionObject> _disposables = [];

        internal Reveal(
            WinUIUIElement element,
            VisualElement targetRegion,
            VisualElement? origin,
            Action applyStateChange,
            uint durationMs,
            int token)
        {
            _element = element;
            _targetRegion = targetRegion;
            _origin = origin;
            _applyStateChange = applyStateChange;
            _durationMs = durationMs;
            _token = token;
        }

        /// <summary>Starts the capture→reveal pipeline. Sync failures rethrow to the click caller
        /// (identical surface behavior to the dev25 synchronous swap).</summary>
        internal void Begin()
        {
            if (_durationMs == 0)
            {
                ApplyOnce();
                Finish();
                return;
            }

            if (!TryResolveHost())
            {
                // Unexpected window tree — no place for the OLD-frame layer ⇒ degrade to the
                // opacity cross-dissolve (rule 3's sanctioned stand-in).
                ApplyOnce();
                StartDip();
                return;
            }

            // Quiesced pre-capture hit: the OLD frame is already in hand, so the whole reveal is
            // built and its render-thread animations STARTED inside this click frame — no capture
            // round-trip on the critical path (plan step 2 "reveal de-block").
            var cached = TryConsumeCache(_anchor, _scale);
            if (cached is not null)
            {
                try
                {
                    BuildAndStart(cached, fromCache: true);
                }
                catch (Exception)
                {
                    // Same guarantee the capture continuation gives: whatever failed mid-build,
                    // the mutation lands exactly once and the half-built surface is restored.
                    ApplyOnce();
                    Finish();
                }

                return;
            }

            // Cold / invalidated cache: the snapshot must be taken BEFORE the swap, so the pipeline
            // continues on a dispatcher continuation; the old state stays painted while it captures.
            _ = CaptureThenRevealAsync();
        }

        // ---------------------------------------------------------------- host resolution

        /// <summary>
        /// Per-invocation host resolution delegated to the shared <see cref="TryResolveAnchor"/>
        /// (same path the quiesce refresh uses, so both key on the identical anchor region).
        /// </summary>
        private bool TryResolveHost()
        {
            if (!TryResolveAnchor(_element, _targetRegion, out var panel, out var anchor, out var scale)
                || panel is null || anchor is null)
            {
                return false;
            }

            _panel = panel;
            _anchor = anchor;
            _scale = scale;
            return true;
        }

        // ---------------------------------------------------------------- capture

        private async Task CaptureThenRevealAsync()
        {
            try
            {
                var snapshot = await CaptureRegionAsync(_anchor!, _scale);

                if (_token != _revealToken)
                {
                    // Superseded while capturing: Finish lands the pending mutation synchronously so
                    // the newest reveal's snapshot sees this reveal's post-state, not a stale frame.
                    Finish();
                    return;
                }

                if (snapshot is null)
                {
                    ApplyOnce();
                    StartDip();
                    return;
                }

                BuildAndStart(snapshot, fromCache: false);
            }
            catch (Exception ex)
            {
                var rethrow = ex;
                try
                {
                    ApplyOnce();
                }
                catch (Exception applyEx)
                {
                    rethrow = applyEx; // the mutation itself failed — surface that, not the decoration
                }

                try
                {
                    Finish(); // idempotent; _applied already set ⇒ pure teardown
                }
                catch (Exception)
                {
                    // teardown swallows; the rethrow below carries the real failure
                }

                RethrowOnDispatcher(rethrow);
            }
        }

        // ---------------------------------------------------------------- build + animate

        private void BuildAndStart(RenderTargetBitmap snapshot, bool fromCache)
        {
            var panel = _panel!;
            var anchor = _anchor!;

            // A cleanup finalizer MUST exist before taking on any visual responsibility: no timer
            // means nobody would ever restore the clip, so swap instantly instead (never leak).
            if (EnsureCleanupTimer() is null)
            {
                ApplyOnce();
                Finish();
                return;
            }

            // ---- geometry math in RAW PIXELS (the compositor's unit space), freshly derived from
            // live runtime sizes + the live RasterizationScale every invocation. Radius grows to
            // the FARTHEST corner of the clipped surface (§4.2 "to the farthest viewport corner"),
            // so the new state fully covers the target at t=end.
            var anchorBounds = new WinUIRect(0d, 0d, anchor.ActualWidth, anchor.ActualHeight);
            var placed = anchor.TransformToVisual(panel).TransformBounds(anchorBounds);

            double originCxDip = anchor.ActualWidth / 2d;
            double originCyDip = anchor.ActualHeight / 2d;
            if (_origin?.Handler?.PlatformView is WinUIFrameworkElement originFe
                && originFe.ActualWidth > 0d
                && originFe.ActualHeight > 0d)
            {
                var tile = originFe.TransformToVisual(anchor)
                    .TransformBounds(new WinUIRect(0d, 0d, originFe.ActualWidth, originFe.ActualHeight));
                originCxDip = tile.X + tile.Width / 2d;
                originCyDip = tile.Y + tile.Height / 2d;
            }

            float scale = (float)_scale;
            var center = new Vector2((float)originCxDip * scale, (float)originCyDip * scale);
            float widthPx = (float)placed.Width * scale;
            float heightPx = (float)placed.Height * scale;
            float spreadX = Math.Max(center.X, widthPx - center.X);
            float spreadY = Math.Max(center.Y, heightPx - center.Y);
            float maxRadius = (float)Math.Sqrt((double)spreadX * spreadX + (double)spreadY * spreadY);

            if (maxRadius < 2f)
            {
                ApplyOnce();
                StartDip();
                return;
            }

            float startRadius = Math.Max(1f, maxRadius * RevealStartFactor);

            // ---- 1) OLD-frame layer, inserted BELOW the live surface's panel child. Hit-test
            // invisible; sized/positioned from the live transform bounds computed just above.
            _snapshotLayer = new WinUICanvas { IsHitTestVisible = false };
            var snapshotImage = new WinUIImage
            {
                Source = snapshot,
                IsHitTestVisible = false,
                Stretch = WinUIStretch.Fill,
                Width = placed.Width,
                Height = placed.Height,
            };
            WinUICanvas.SetLeft(snapshotImage, placed.X);
            WinUICanvas.SetTop(snapshotImage, placed.Y);
            _snapshotLayer.Children.Add(snapshotImage);
            panel.Children.Insert(panel.Children.IndexOf(anchor), _snapshotLayer);

            // ---- 2) Radial clip on the LIVE surface at the small start radius — BEFORE the swap,
            // so no unclipped new-state frame and no backdrop frame can ever render.
            _clippedVisual = ElementCompositionPreview.GetElementVisual(anchor);
            var compositor = _clippedVisual.Compositor;

            var geometry = compositor.CreateEllipseGeometry();
            geometry.Center = center;
            geometry.Radius = new Vector2(startRadius);
            _disposables.Add(geometry);

            var clip = compositor.CreateGeometricClip(geometry);
            _clip = clip;
            _disposables.Add(clip);
            _clippedVisual.Clip = clip;

            // ---- 3) The caller's single, atomic mutation lands (live surface = NEW state).
            ApplyOnce();

            // ---- 4) Orbit ring on a compositor ShapeVisual, riding the wavefront above
            // everything, colored with the NOW-active brand (§4.3: 3px stroke + 45% glow).
            _ringLayer = new WinUICanvas { IsHitTestVisible = false };
            _ringHost = new WinUICanvas
            {
                IsHitTestVisible = false,
                Width = placed.Width,
                Height = placed.Height,
            };
            WinUICanvas.SetLeft(_ringHost, placed.X);
            WinUICanvas.SetTop(_ringHost, placed.Y);
            _ringLayer.Children.Add(_ringHost);

            var brand = ToWinUIColor(BrandPalette.Brand, 1f);
            var glow = ToWinUIColor(BrandPalette.Brand, RingGlowAlpha);
            var ringBrush = compositor.CreateColorBrush(brand);
            var glowBrush = compositor.CreateColorBrush(glow);
            _disposables.Add(ringBrush);
            _disposables.Add(glowBrush);

            var shapeVisual = compositor.CreateShapeVisual();
            shapeVisual.Size = new Vector2(widthPx, heightPx);

            // WinUI3 compositors hand out shapes only as sprite shapes wrapping a geometry — so
            // each ring stroke rides its OWN CompositionEllipseGeometry, the exact same object
            // type (and the exact same "Radius" d-property channel) the clip reveal animates.
            var glowGeometry = compositor.CreateEllipseGeometry();
            glowGeometry.Center = center;
            glowGeometry.Radius = new Vector2(startRadius);
            var ringGeometry = compositor.CreateEllipseGeometry();
            ringGeometry.Center = center;
            ringGeometry.Radius = new Vector2(startRadius);

            var glowShape = compositor.CreateSpriteShape(glowGeometry);
            glowShape.StrokeThickness = RingGlowStrokeDips * scale;
            glowShape.StrokeBrush = glowBrush;

            var ringShape = compositor.CreateSpriteShape(ringGeometry);
            ringShape.StrokeThickness = RingStrokeDips * scale;
            ringShape.StrokeBrush = ringBrush;

            shapeVisual.Shapes.Add(glowShape);
            shapeVisual.Shapes.Add(ringShape);
            _disposables.Add(glowGeometry);
            _disposables.Add(ringGeometry);
            _disposables.Add(glowShape);
            _disposables.Add(ringShape);
            _disposables.Add(shapeVisual);

            ElementCompositionPreview.SetElementChildVisual(_ringHost, shapeVisual);
            panel.Children.Add(_ringLayer);

            // ---- 5) The render-thread animations. Four key-frame animations (clip radius,
            // both ring-geometry radii as Vector2, ring opacity as scalar), one shared ease-in-out
            // — zero per-frame managed work, all started on composition objects (gate 3).
            var easeInOut = compositor.CreateCubicBezierEasingFunction(
                new Vector2(0.42f, 0.0f), new Vector2(0.58f, 1.0f));
            _disposables.Add(easeInOut);
            var duration = TimeSpan.FromMilliseconds(_durationMs);

            var radiusAnimation = CreateRadiusAnimation(compositor, duration, startRadius, maxRadius, easeInOut);
            _disposables.Add(radiusAnimation);
            geometry.StartAnimation(RadiusPropertyName, radiusAnimation);
            _started.Add((geometry, RadiusPropertyName));

            var ringRadius = CreateRadiusAnimation(compositor, duration, startRadius, maxRadius, easeInOut);
            _disposables.Add(ringRadius);
            ringGeometry.StartAnimation(RadiusPropertyName, ringRadius);
            _started.Add((ringGeometry, RadiusPropertyName));

            var glowRadius = CreateRadiusAnimation(compositor, duration, startRadius, maxRadius, easeInOut);
            _disposables.Add(glowRadius);
            glowGeometry.StartAnimation(RadiusPropertyName, glowRadius);
            _started.Add((glowGeometry, RadiusPropertyName));

            // Ring dissolves over the last 25% of the reveal (mockup ring-grow end-fade).
            var fade = compositor.CreateScalarKeyFrameAnimation();
            fade.Duration = duration;
            fade.InsertKeyFrame(0.0f, 1.0f);
            fade.InsertKeyFrame(RingFadeStartNormalized, 1.0f);
            fade.InsertKeyFrame(1.0f, 0.0f, easeInOut);
            _disposables.Add(fade);

            shapeVisual.StartAnimation(OpacityPropertyName, fade);
            _started.Add((shapeVisual, OpacityPropertyName));

            ArmCleanup(_durationMs);

            // Click→reveal-start evidence (plan step 5): "cached" proves the pre-capture path
            // (reveal began inside the click frame, no capture on it); "captured" is the cold rung.
            MostaqlK.Services.Diagnostics.InteractionLogger.Mark(
                "Morph.RevealStarted", fromCache ? "cached" : "captured", new { durationMs = _durationMs });
        }

        /// <summary>
        /// The one key-frame shape used by the reveal: grow a composition object's <c>Radius</c>
        /// d-property (the clip's ellipse geometry and each ring sprite geometry — all
        /// <see cref="CompositionEllipseGeometry"/>, whose <c>Radius</c> is a <see cref="Vector2"/>
        /// of the two semi-axes in WinUI 3) from the collapsed start circle to the farthest-corner
        /// distance with the mockup's ease-in-out. A plain property key-frame animation on a
        /// compositor object: the render-thread class animation-safety rule 3 approves, not a
        /// transform.
        /// </summary>
        private static CompositionAnimation CreateRadiusAnimation(
            Compositor compositor,
            TimeSpan duration,
            float startRadius,
            float maxRadius,
            CompositionEasingFunction easeInOut)
        {
            var animation = compositor.CreateVector2KeyFrameAnimation();
            animation.Duration = duration;
            animation.InsertKeyFrame(0.0f, new Vector2(startRadius));
            animation.InsertKeyFrame(1.0f, new Vector2(maxRadius), easeInOut);
            return animation;
        }

        // ---------------------------------------------------------------- degraded dissolve

        private void StartDip()
        {
            try
            {
                var visual = ElementCompositionPreview.GetElementVisual(_element);
                var compositor = visual.Compositor;

                if (EnsureCleanupTimer() is null)
                {
                    Finish();
                    return;
                }

                var animation = compositor.CreateScalarKeyFrameAnimation();
                animation.Duration = TimeSpan.FromMilliseconds(_durationMs);
                var easeInOut = compositor.CreateCubicBezierEasingFunction(
                    new Vector2(0.42f, 0.0f), new Vector2(0.58f, 1.0f));
                animation.InsertKeyFrame(0.0f, 1.0f);
                animation.InsertKeyFrame(0.45f, DipOpacity, easeInOut);
                animation.InsertKeyFrame(1.0f, 1.0f, easeInOut);

                visual.Opacity = 1.0f; // static base to which the property reverts on stop
                visual.StartAnimation(OpacityPropertyName, animation);

                _dipVisual = visual;
                _started.Add((visual, OpacityPropertyName));
                _disposables.Add(animation);
                _disposables.Add(easeInOut);

                ArmCleanup(_durationMs);

                // Degraded rung — evidence that the effect still started in the invoking frame.
                MostaqlK.Services.Diagnostics.InteractionLogger.Mark(
                    "Morph.RevealStarted", "dissolve", new { durationMs = _durationMs });
            }
            catch (Exception)
            {
                // A decorative dissolve must never take the app down; the mutation already landed.
                Finish();
            }
        }

        // ---------------------------------------------------------------- mutation + landing

        private void ApplyOnce()
        {
            if (_applied)
            {
                return;
            }

            // Flag FIRST: a throwing mutation is surfaced but NEVER retried (exactly-once, even
            // when several teardown paths touch the same reveal).
            _applied = true;
            _applyStateChange();
        }

        /// <summary>
        /// The single idempotent landing point: applies a still-pending mutation (a reveal
        /// superseded before its swap must never lose the user's intent), then restores EVERYTHING —
        /// stopped compositor animations, clip removed from the live visual (CRITICAL: a leaked clip
        /// would break all later repaints of the page), overlay layers removed from the panel, all
        /// composition objects disposed ON THE UI THREAD (never left for the CLR finalizer — the
        /// postmortem's fail-fast class). Safe to call from the completion tick, a fast-finish, or
        /// any catch path.
        /// </summary>
        internal void Finish()
        {
            if (_finished)
            {
                return;
            }

            _finished = true;

            try
            {
                if (!_applied)
                {
                    ApplyOnce();
                }
            }
            finally
            {
                Teardown();
                if (ReferenceEquals(_current, this))
                {
                    _current = null;
                }

                CompleteReveal();

                // Every landing path (completion tick, fast-finish, duration-0, no-timer fallback,
                // any catch) funnels through here: the surface is settled NEW state now, so queue
                // the one-shot quiesce re-capture that refills the pre-capture cache.
                ScheduleQuiescedRefresh();
            }
        }

        private void Teardown()
        {
            foreach (var (target, property) in _started)
            {
                try
                {
                    target.StopAnimation(property);
                }
                catch (Exception)
                {
                    // best-effort release; object may already be torn down with the visual tree
                }
            }

            _started.Clear();

            try
            {
                // Restore the live surface ONLY if our clip is still the one attached (never
                // clobber a clip some other owner set meanwhile).
                if (_clippedVisual is { } visual && _clip is not null && ReferenceEquals(visual.Clip, _clip))
                {
                    visual.Clip = null;
                }
            }
            catch (Exception)
            {
                // visual tree already gone — nothing to restore
            }

            try
            {
                if (_dipVisual is { } dip)
                {
                    dip.Opacity = 1.0f;
                }
            }
            catch (Exception)
            {
                // same as above
            }

            // Remove the overlay layers (idempotent; a window close mid-morph simply no-ops).
            try
            {
                if (_ringHost is { } host)
                {
                    ElementCompositionPreview.SetElementChildVisual(host, null!);
                }

                if (_snapshotLayer is { } snapshotLayer && snapshotLayer.Parent is WinUIPanel snapshotPanel)
                {
                    snapshotPanel.Children.Remove(snapshotLayer);
                }

                if (_ringLayer is { } ringLayer && ringLayer.Parent is WinUIPanel ringPanel)
                {
                    ringPanel.Children.Remove(ringLayer);
                }
            }
            catch (Exception)
            {
                // best-effort layer removal
            }

            _snapshotLayer = null;
            _ringLayer = null;
            _ringHost = null;
            _clippedVisual = null;
            _clip = null;
            _dipVisual = null;
            _panel = null;
            _anchor = null;

            foreach (var disposable in _disposables)
            {
                try
                {
                    disposable.Dispose();
                }
                catch (Exception)
                {
                    // best-effort release; a composition object may already be gone
                }
            }

            _disposables.Clear();
        }

        private static WinUIColor ToWinUIColor(Color color, float alphaOverride)
        {
            var alpha = Math.Clamp(color.Alpha * alphaOverride, 0f, 1f);
            return WinUIColor.FromArgb(
                (byte)Math.Round(alpha * 255d),
                (byte)Math.Round(color.Red * 255d),
                (byte)Math.Round(color.Green * 255d),
                (byte)Math.Round(color.Blue * 255d));
        }

        private static void RethrowOnDispatcher(Exception exception)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher is null)
            {
                return; // nowhere to surface it; the mutation-landing guarantee already held
            }

            dispatcher.Dispatch(() => ExceptionDispatchInfo.Capture(exception).Throw());
        }
    }
}
