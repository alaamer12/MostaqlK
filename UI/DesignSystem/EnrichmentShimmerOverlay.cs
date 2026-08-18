using MostaqlK.UI.PlatformComponents;

namespace MostaqlK.UI.DesignSystem;

/// <summary>
/// Full-card overlay that sweeps a soft, angled light reflection across a project card while its
/// enrichment is still in progress (<c>ProjectCardViewModel.IsEnriching</c>). Unlike
/// <see cref="ShimmerBox"/> (an opaque skeleton placeholder that hides not-yet-loaded content)
/// this overlay sits transparently *on top* of the already-visible, fully-readable card — a
/// diagonal translucent band continuously sweeps from one side to the other, evoking a passing
/// light reflection rather than a loading skeleton. Sits inert (zero opacity, not ticking,
/// <see cref="InputTransparent"/>) whenever <see cref="IsActive"/> is false, so it never
/// intercepts taps and never re-renders the rest of the feed.
/// Respects <see cref="MotionPreferences.IsReducedMotionRequested"/> like
/// <see cref="NewRibbonBadge"/>: the reflection is simply not shown (no static replacement is
/// needed since, unlike the ribbon, this overlay carries no information of its own).
/// </summary>
/// <remarks>
/// The feed can have dozens of cards "enriching" (and therefore animating) at once. An earlier
/// version drove each card's sweep with its own independent <c>TranslateToAsync</c> loop -
/// i.e. up to dozens of concurrently running native WinUI composition animations starting at
/// nearly the same instant during startup. That reproducibly crashed the whole app natively
/// (WinUI/<c>combase.dll</c> access violation, <c>STATUS_STOWED_EXCEPTION</c>) - a scale problem
/// with that many simultaneous per-view native animation objects, not something a managed
/// try/catch can guard against. This version instead uses a single shared ticker
/// (<see cref="Microsoft.Maui.Animations.Ticker"/> via one app-wide ticking callback) that
/// directly computes and assigns every active overlay's <see cref="View.TranslationX"/> each
/// frame from elapsed time - there is exactly one native animation driver for the whole app
/// no matter how many cards are shimmering at once, eliminating the concurrency that caused the
/// crash while keeping the same continuous, eased, angled sweep look.
///
/// Cross-platform note: the fix itself is built entirely on <see cref="IDispatcherTimer"/>
/// (<c>dispatcher.CreateTimer()</c>), MAUI's standard cross-platform timer abstraction — there is
/// no <c>#if WINDOWS</c>/WinUI-specific API anywhere in this class. So although the underlying bug
/// that motivated it was Windows/WinUI-only, this workaround does NOT get a <c>.Windows.cs</c>
/// split: it has no platform-conditional branch to isolate, and the single-shared-ticker design is
/// a reasonable, harmless choice on every platform (avoiding dozens of independent native
/// animation drivers is not a Windows-only concern).
/// </remarks>
public class EnrichmentShimmerOverlay : ContentView
{
    public static readonly BindableProperty IsActiveProperty = BindableProperty.Create(
        nameof(IsActive),
        typeof(bool),
        typeof(EnrichmentShimmerOverlay),
        defaultValue: false,
        propertyChanged: OnIsActiveChanged);

    /// <summary>Bound to <c>ProjectCardViewModel.IsEnriching</c> — shows/hides the reflection sweep.</summary>
    public bool IsActive
    {
        get => (bool)GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    /// <summary>One full sweep pass, in milliseconds - matches the previous per-instance animation's duration.</summary>
    private const double PassDurationMs = 2600;

    /// <summary>All currently-active overlays still attached to the visual tree, ticked by <see cref="SharedTicker"/>.</summary>
    private static readonly List<EnrichmentShimmerOverlay> ActiveOverlays = [];
    private static IDispatcherTimer? _sharedTicker;
    private static long _tickStartTicks;

    private readonly BoxView _band;
    private double _passStartMs;
    private bool _isTicking;

    public EnrichmentShimmerOverlay()
    {
        InputTransparent = true;
        IsClippedToBounds = true;
        Opacity = 0;

        // A single diagonal band using a soft gradient (transparent -> translucent white ->
        // transparent) so its edges fade in/out instead of showing a hard-edged rectangle
        // sweeping across the card - this is what reads as a "light reflection" rather than a
        // flashing bar. Rotated so it crosses the card at an angle, wider/taller than the card
        // itself so the rotated band still fully covers it edge-to-edge as it sweeps.
        _band = new BoxView
        {
            WidthRequest = 90,
            Rotation = 18,
            InputTransparent = true,
            Background = new LinearGradientBrush(
                [
                    new GradientStop(Color.FromArgb("#00FFFFFF"), 0f),
                    new GradientStop(Color.FromArgb("#33FFFFFF"), 0.5f),
                    new GradientStop(Color.FromArgb("#00FFFFFF"), 1f),
                ],
                new Point(0, 0),
                new Point(1, 0)),
        };

        Content = new Grid
        {
            IsClippedToBounds = true,
            Children = { _band },
        };

        Loaded += (_, _) => StartTickingIfNeeded();
        Unloaded += (_, _) => StopTicking();
    }

    private static void OnIsActiveChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var overlay = (EnrichmentShimmerOverlay)bindable;
        var isActive = (bool)newValue;

        // Enrichment finished (successfully or not): the sweep must stop immediately and cleanly
        // rather than fading out mid-pass, restoring the plain card appearance right away.
        overlay.Opacity = isActive ? 1 : 0;
        if (!isActive)
        {
            overlay.StopTicking();
            return;
        }

        overlay.StartTickingIfNeeded();
    }

    private void StartTickingIfNeeded()
    {
        if (_isTicking || !IsActive || !IsLoaded || MotionPreferences.IsReducedMotionRequested)
        {
            return;
        }

        _isTicking = true;
        _passStartMs = CurrentTicker.ElapsedMs;
        lock (ActiveOverlays)
        {
            ActiveOverlays.Add(this);
        }

        EnsureSharedTickerRunning();
    }

    private void StopTicking()
    {
        if (!_isTicking)
        {
            return;
        }

        _isTicking = false;
        lock (ActiveOverlays)
        {
            ActiveOverlays.Remove(this);
        }
    }

    /// <summary>
    /// Advances this overlay's band by one shared-ticker frame. Never throws outward: a card can
    /// be recycled/detached by the feed's live reconciliation between one tick and the next, so a
    /// stale reference hitting a torn-down handler must be tolerated rather than propagate as an
    /// unhandled exception.
    /// </summary>
    private void Tick(double nowMs)
    {
        try
        {
            if (!IsActive || !IsLoaded || Handler is null)
            {
                return;
            }

            var travel = Width + _band.WidthRequest;
            if (travel <= 0)
            {
                return;
            }

            var elapsedInPass = (nowMs - _passStartMs) % PassDurationMs;
            if (elapsedInPass < 0)
            {
                elapsedInPass = 0;
            }

            var progress = elapsedInPass / PassDurationMs;
            var eased = Easing.SinInOut.Ease(progress);
            // Sweep from fully off-screen on the left to fully off-screen on the right, so the
            // wrap-around between passes is invisible (both endpoints sit outside the clipped
            // bounds), reading as one continuous, uninterrupted sweep.
            _band.TranslationX = (eased * (travel + _band.WidthRequest)) - _band.WidthRequest;
        }
        catch (Exception)
        {
            // See class remarks: purely decorative, so a stale/disposed handler must never
            // surface as a crash - just stop ticking this instance.
            StopTicking();
        }
    }

    private static void EnsureSharedTickerRunning()
    {
        if (_sharedTicker is not null || Application.Current?.Dispatcher is not { } dispatcher)
        {
            return;
        }

        _tickStartTicks = Environment.TickCount64;
        _sharedTicker = dispatcher.CreateTimer();
        _sharedTicker.Interval = TimeSpan.FromMilliseconds(33); // ~30fps - smooth enough for a slow, subtle sweep.
        _sharedTicker.Tick += (_, _) =>
        {
            var nowMs = (double)(Environment.TickCount64 - _tickStartTicks);
            EnrichmentShimmerOverlay[] snapshot;
            lock (ActiveOverlays)
            {
                if (ActiveOverlays.Count == 0)
                {
                    return;
                }

                snapshot = ActiveOverlays.ToArray();
            }

            foreach (var overlay in snapshot)
            {
                overlay.Tick(nowMs);
            }
        };
        _sharedTicker.Start();
    }

    private static class CurrentTicker
    {
        public static double ElapsedMs => Environment.TickCount64 - _tickStartTicks;
    }
}
