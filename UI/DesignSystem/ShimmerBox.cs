using MostaqlK.UI.PlatformComponents;

namespace MostaqlK.UI.DesignSystem;

/// <summary>
/// Animated shimmer placeholder used for skeleton loading states, paired 1:1 with every content
/// element while data is loading (see system-components.md, section 13.3). Renders a rounded
/// flat box with a translucent overlay that sweeps left-to-right on a loop while attached to the
/// visual tree, approximating the "SkeletonBase/SkeletonShimmer" color tokens until the full
/// Design System token set lands.
/// </summary>
/// <remarks>
/// <b>Shared-ticker mechanism (dev31 crash fix — do not regress to per-instance animation loops):</b>
/// an earlier version drove every box with its own <c>while(true) { await TranslateToAsync(...) }</c>
/// loop. That is exactly the pattern the <see cref="EnrichmentShimmerOverlay"/> remarks document as
/// the postmortem: dozens of concurrent per-view native WinUI composition animations starting at
/// nearly the same instant reproducibly fail-fast the process natively
/// (<c>STATUS_STOWED_EXCEPTION</c> / 0xC000027B in <c>combase.dll</c> / <c>Microsoft.UI.Xaml.dll</c>,
/// uncatchable managed-side). The dev31 continuous-row feed realized five skeleton boxes per card
/// (~40 loops at once) and crashed 15–26 s after every projects-page launch. This version instead
/// uses ONE app-wide <see cref="IDispatcherTimer"/> (same design as <see cref="EnrichmentShimmerOverlay"/>)
/// that assigns <c>TranslationX</c> directly from elapsed time each frame — there is exactly one
/// native animation driver no matter how many boxes are on screen — and a box only participates
/// while it is attached AND effectively visible (its own and every ancestor
/// <see cref="VisualElement.IsVisible"/> true, walked per tick: the feed's collapsed skeleton
/// regions keep their children attached with <c>Loaded</c> already fired, so a per-instance gate on
/// the box's OWN flag would never stop them). The sweep look is unchanged: linear left-to-right
/// pass over 1400 ms, 200 ms pause, repeat; parked (never ticking) while ambient motion is
/// suspended (window hidden to the tray), matching the previous behavior.
/// </remarks>
public class ShimmerBox : ContentView
{
    /// <summary>One sweep pass, milliseconds — identical to the old per-instance <c>TranslateToAsync(…, 1400, Linear)</c>.</summary>
    private const double SweepDurationMs = 1400;

    /// <summary>Rest between passes — identical to the old <c>await Task.Delay(200)</c>.</summary>
    private const double PauseDurationMs = 200;

    private const double CycleDurationMs = SweepDurationMs + PauseDurationMs;

    private readonly BoxView _baseBox;
    private readonly BoxView _sweep;

    /// <summary>All attached, effectively-visible boxes, ticked by the single <see cref="_sharedTicker"/>.</summary>
    private static readonly List<ShimmerBox> ActiveBoxes = [];
    private static IDispatcherTimer? _sharedTicker;
    private static long _startTicks;

    private bool _registered;

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

        Loaded += (_, _) => Register();
        Unloaded += (_, _) => Unregister();
    }

    private void Register()
    {
        if (_registered || MotionPreferences.IsReducedMotionRequested)
        {
            return;
        }

        _registered = true;
        lock (ActiveBoxes)
        {
            ActiveBoxes.Add(this);
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
        lock (ActiveBoxes)
        {
            ActiveBoxes.Remove(this);
        }
    }

    /// <summary>
    /// Advances the sweep by one shared-ticker frame. Never throws outward: a card can be
    /// recycled/detached by the feed's live reconciliation between one tick and the next, so a
    /// stale reference hitting a torn-down handler must be tolerated rather than propagate
    /// (same contract as <see cref="EnrichmentShimmerOverlay.Tick"/>).
    /// </summary>
    private void Tick(double nowMs)
    {
        try
        {
            if (!IsEffectivelyVisible() || Handler is null)
            {
                return;
            }

            // Window hidden in the tray: park the sweep at its start instead of animating an
            // invisible view; the shared ticker resumes it on restore.
            if (MostaqlK.UI.PlatformComponents.MotionPreferences.IsAmbientMotionSuspended)
            {
                _sweep.TranslationX = 0;
                return;
            }

            var travel = Math.Max(0, Width - _sweep.Width);
            if (travel <= 0)
            {
                return;
            }

            var elapsedInCycle = nowMs % CycleDurationMs;
            if (elapsedInCycle >= SweepDurationMs)
            {
                // The old loop's 200 ms pause: the sweep rests off the leading edge.
                _sweep.TranslationX = 0;
                return;
            }

            _sweep.TranslationX = travel * (elapsedInCycle / SweepDurationMs);
        }
        catch (Exception)
        {
            // Purely decorative — a stale/disposed handler must never surface as a crash.
            Unregister();
        }
    }

    /// <summary>
    /// True when this box and every ancestor up to the page are visible. MAUI maps
    /// <c>IsVisible=false</c> to a collapsed platform element that KEEPS its children attached
    /// (their own <c>IsVisible</c> stays true, <c>Loaded</c> already fired), which is exactly how
    /// the feed's collapsed skeleton regions used to keep dozens of invisible loops running.
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
        _sharedTicker.Interval = TimeSpan.FromMilliseconds(33); // ~30fps — smooth enough for the slow sweep.
        _sharedTicker.Tick += (_, _) =>
        {
            var nowMs = (double)(Environment.TickCount64 - _startTicks);
            ShimmerBox[] snapshot;
            lock (ActiveBoxes)
            {
                if (ActiveBoxes.Count == 0)
                {
                    return;
                }

                snapshot = ActiveBoxes.ToArray();
            }

            foreach (var box in snapshot)
            {
                box.Tick(nowMs);
            }
        };
        _sharedTicker.Start();
    }
}
