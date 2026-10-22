using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml.Hosting;
using WinUIDependencyObject = Microsoft.UI.Xaml.DependencyObject;
using WinUIElement = Microsoft.UI.Xaml.UIElement;
using WinUIListViewBase = Microsoft.UI.Xaml.Controls.ListViewBase;
using WinUIVisual = Microsoft.UI.Composition.Visual;
using WinUIVisualTreeHelper = Microsoft.UI.Xaml.Media.VisualTreeHelper;

namespace MostaqlK.UI.PlatformComponents.ContentSettle;

/// <summary>
/// Windows half of <see cref="ContentSettle"/>: drives the compositor-level
/// <see cref="Microsoft.UI.Composition.Visual"/>.<c>Offset</c> (a <see cref="Vector3"/>) of each
/// realized item card with a single, finite <see cref="Vector3KeyFrameAnimation"/> started on the
/// render thread (<see cref="ElementCompositionPreview.GetElementVisual"/>). This is the
/// animation-safety-sanctioned stand-in for the mockup's CSS <c>card-settle</c>: it touches the
/// compositor's own <c>Offset</c> vector — the position property of the visual — NOT a XAML
/// <c>RenderTransform</c>, so it churns ZERO managed <c>CompositeTransform</c>/<c>ScaleTransform</c>
/// objects, which is exactly the per-frame allocation the long-idle background-crash postmortem
/// (<c>TransformReuseMapping</c>) identifies as fatal. The 50 ms stagger lives inside the one keyframe
/// object (a start-hold keyframe parks the card at the dip until its slot, then it travels to rest),
/// so there is no <c>DispatcherTimer</c> and no per-frame managed callback — the whole pass is a
/// finite, abortable one-shot (guardrail rules 2+3).
/// <para>
/// Realized cards come from the feed's own WinUI <see cref="WinUIListViewBase"/>.<c>ContainerFromIndex</c>
/// (MAUI's Windows <c>CollectionView</c> is hosted on the WinUI items infrastructure) after a forced
/// <c>UpdateLayout</c>; virtualized (off-screen) slots simply return null and are skipped, so only the
/// visible cards settle — matching "stagger only the first N visible items." Any failure (no handler,
/// no items host, no realized container, a compositor exception) leaves the rows at rest: the
/// decorative settle is skipped, the app is never taken down by it.
/// </para>
/// </summary>
public static partial class ContentSettle
{
    // The single compositor property this unit animates: the visual's position OFFSET vector. It is a
    // plain transform property of the compositor visual, NOT a XAML RenderTransform — which is what
    // makes the mechanism compliant with animation-safety.md rule 3.
    private const string OffsetPropertyName = "Offset";

    // How many stagger slots fit under the ~600 ms ceiling: (600 - 340) / 50 = 5 (delays 0..250 ms).
    private const int MaxStaggerSlots = (int)((SettleMaxTotalMs - SettleDurationMs) / SettleStaggerMs);

    // Visuals currently driven by the in-flight pass — the fast-finish target set (re-entrancy).
    // Every new pass calls AbortInFlightCore first, which stops + releases these and clears the list,
    // so at most SettleMaxItems compositor animations are ever live.
    private static readonly List<WinUIVisual> _runningVisuals = [];

    static partial void PerformSettle(CollectionView feed)
    {
        // Re-entrancy: a new settle always wins — land anything still mid-travel at rest first, then
        // start the new pass, so a rapid double-switch never stacks two passes on one card.
        AbortInFlightCore();

        if (feed.Handler?.PlatformView is not WinUIDependencyObject platformRoot)
        {
            return; // Handler not realized yet — nothing to animate; rows show at rest (instant).
        }

        WinUIListViewBase? host = ResolveItemsHost(platformRoot);
        if (host is null)
        {
            return; // No suitable compositor path — instant, per the guardrail's escape hatch.
        }

        try
        {
            // Force a layout so the first viewport's item containers are realized; without this a
            // just-swapped ItemsSource can return null from ContainerFromIndex for every index.
            host.UpdateLayout();

            for (int i = 0; i < SettleMaxItems; i++)
            {
                // ContainerFromIndex returns null for not-yet-realized (virtualized) slots — skipped,
                // so only the visible cards animate.
                if (host.ContainerFromIndex(i) is WinUIElement container)
                {
                    AnimateContainer(container, i);
                }
            }
        }
        catch (Exception)
        {
            // A decorative mount pass must never take the app down; land at rest and move on.
            AbortInFlightCore();
        }
    }

    static partial void AbortInFlightCore()
    {
        if (_runningVisuals.Count == 0)
        {
            return;
        }

        foreach (var visual in _runningVisuals)
        {
            try
            {
                // Disconnect the held animation; Offset reverts to its static base value (0,0,0), so no
                // card is ever parked mid-travel after a cancel.
                visual.StopAnimation(OffsetPropertyName);
            }
            catch (Exception)
            {
                // best-effort release; a visual may already be torn down with its container.
            }
        }

        _runningVisuals.Clear();
    }

    /// <summary>
    /// Starts the one-shot dip-and-rise on a single realized card container: an 8 px downward offset
    /// that travels up to rest over <see cref="SettleDurationMs"/> with the mockup's cubic-bezier,
    /// delayed by <see cref="SettleStaggerMs"/> per index (capped at <see cref="MaxStaggerSlots"/>) via
    /// a start-hold keyframe so the whole stagger is one finite animation object rather than a
    /// timer-driven loop.
    /// </summary>
    private static void AnimateContainer(WinUIElement container, int index)
    {
        WinUIVisual visual = ElementCompositionPreview.GetElementVisual(container);
        Compositor compositor = visual.Compositor;

        int delayMs = Math.Min(index, MaxStaggerSlots) * (int)SettleStaggerMs;
        uint totalMs = SettleDurationMs + (uint)delayMs;

        var animation = compositor.CreateVector3KeyFrameAnimation();
        animation.Duration = TimeSpan.FromMilliseconds(totalMs);

        // Compositor Y grows downward, so the start dip (+8) sits below rest and travels UP to zero —
        // exactly `translateY(8px) -> none` from the mockup keyframe.
        var dip = new Vector3(0f, (float)SettleOffsetPx, 0f);
        animation.InsertKeyFrame(0f, dip);

        if (delayMs > 0)
        {
            // Hold the card at the dip for its stagger slot, then release it toward rest.
            animation.InsertKeyFrame((float)delayMs / totalMs, dip);
        }

        // cubic-bezier(.22,.61,.36,1) applied on the travel segment (from the dip/hold into rest).
        var ease = compositor.CreateCubicBezierEasingFunction(
            new Vector2(EasingX1, EasingY1), new Vector2(EasingX2, EasingY2));
        animation.InsertKeyFrame(1f, Vector3.Zero, ease);

        visual.StartAnimation(OffsetPropertyName, animation);
        _runningVisuals.Add(visual);

        // No Completed hook is attached: the animation is a non-looping one-shot whose FINAL keyframe
        // is the rest value, so once it plays the card is already back at Offset 0 (never a stuck
        // offset), and the compositor does no per-frame managed work while it holds. The held
        // animation is released by AbortInFlightCore on the next pass (rapid switch) or when the item
        // container is recycled — bounded to SettleMaxItems live visuals at a time. Hooking
        // AnimationController.Completed is intentionally avoided: the animation-safety surface forbids
        // any per-frame/callback transform machinery, and the terminal-rest keyframe makes cleanup
        // deterministic enough without it.
    }

    /// <summary>
    /// Resolves the feed's WinUI items host from the <c>CollectionView</c>'s platform view. MAUI's
    /// Windows items controls sit on <see cref="WinUIListViewBase"/>; the handler may expose it
    /// directly or wrap it in a template part, so a one-shot (never per-frame) visual-tree probe is the
    /// fallback. Returns null when nothing suitable is found ⇒ the caller settles nothing (instant).
    /// </summary>
    private static WinUIListViewBase? ResolveItemsHost(WinUIDependencyObject root, int depth = 0)
    {
        if (root is WinUIListViewBase direct)
        {
            return direct;
        }

        if (depth > 12)
        {
            return null;
        }

        int children = WinUIVisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < children; i++)
        {
            if (WinUIVisualTreeHelper.GetChild(root, i) is not WinUIDependencyObject childDo)
            {
                continue;
            }

            if (ResolveItemsHost(childDo, depth + 1) is { } found)
            {
                return found;
            }
        }

        return null;
    }
}
