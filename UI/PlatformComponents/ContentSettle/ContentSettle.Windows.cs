using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml.Controls;
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
/// <b>Realization (v3 plan Step 4 re-timing):</b> cards come from the feed's own WinUI
/// <see cref="WinUIListViewBase"/> container enumeration WITHOUT the former forced
/// <c>UpdateLayout()</c> — a synchronous full-layout pass on the settle frame was the last layout
/// cost the morph story still paid. Containers that are already realized animate immediately;
/// anything not realized yet is picked up by the item-realization affordance
/// (<see cref="ListViewBase.ContainerContentChanging"/>) as the WinUI virtualizer lands it, plus
/// one next-dispatcher-frame retry. No timers: the subscription is one pass, detached on
/// completion, on the next settle (fast-finish), or when the host dies with the page. Any failure
/// (no handler, no items host, a compositor exception) leaves the rows at rest: the decorative
/// settle is skipped, the app is never taken down by it.
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

    // The in-flight pass waiting on container realization (v3 Step 4: realize-off-the-event, never
    // a forced layout). One at a time — a new settle supersedes (and detaches) the previous.
    private sealed record SettlePass(WinUIListViewBase Host, HashSet<object> Animated)
    {
        public int AnimatedCount;
    }

    private static SettlePass? _pendingPass;

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
            var pass = new SettlePass(host, []);

            // Enumerate CURRENTLY realized containers only (no forced layout — a just-swapped
            // ItemsSource may realize next frame; the event below catches those).
            TryAnimatePending(pass);

            if (!IsPassSatisfied(pass))
            {
                _pendingPass = pass;
                host.ContainerContentChanging += OnContainerContentChanging;

                // One next-dispatcher-frame retry (single post, not a per-frame timer): covers the
                // case where realization completed between the enumeration above and the subscription.
                _ = feed.Dispatcher.Dispatch(() =>
                {
                    if (ReferenceEquals(_pendingPass, pass))
                    {
                        try
                        {
                            TryAnimatePending(pass);
                        }
                        catch (Exception)
                        {
                            DetachPass(pass);
                        }

                        if (IsPassSatisfied(pass))
                        {
                            DetachPass(pass);
                        }
                    }
                });
            }
        }
        catch (Exception)
        {
            // A decorative mount pass must never take the app down; land at rest and move on.
            AbortInFlightCore();
        }
    }

    /// <summary>
    /// WinUI item-realization callback: animates each newly realized container in the settle budget
    /// as it lands. Detaches once the visible window has been covered (bounded work per pass).
    /// </summary>
    private static void OnContainerContentChanging(WinUIListViewBase sender, ContainerContentChangingEventArgs args)
    {
        var pass = _pendingPass;
        if (pass is null || !ReferenceEquals(pass.Host, sender))
        {
            return; // not this unit's host (or the pass already finished)
        }

        try
        {
            if (args.ItemIndex >= 0
                && args.ItemIndex < SettleMaxItems
                && sender.ContainerFromIndex(args.ItemIndex) is WinUIElement container
                && pass.Animated.Add(container))
            {
                AnimateContainer(container, args.ItemIndex);
                pass.AnimatedCount++;
            }
        }
        catch (Exception)
        {
            DetachPass(pass);
            return;
        }

        if (IsPassSatisfied(pass))
        {
            DetachPass(pass);
        }
    }

    /// <summary>Animates every not-yet-animated container currently realized within the settle budget.</summary>
    private static void TryAnimatePending(SettlePass pass)
    {
        var host = pass.Host;
        int total = Math.Min(host is Microsoft.UI.Xaml.Controls.ItemsControl ic ? ic.Items?.Count ?? 0 : 0, SettleMaxItems);
        for (int i = 0; i < total; i++)
        {
            // ContainerFromIndex returns null for not-yet-realized (virtualized) slots — skipped and
            // picked up later via ContainerContentChanging, so only visible cards ever animate.
            if (host.ContainerFromIndex(i) is WinUIElement container && pass.Animated.Add(container))
            {
                AnimateContainer(container, i);
                pass.AnimatedCount++;
            }
        }
    }

    /// <summary>True when every in-budget item that can be realized has been animated (nothing pending).</summary>
    private static bool IsPassSatisfied(SettlePass pass)
    {
        int total = Math.Min(
            pass.Host is Microsoft.UI.Xaml.Controls.ItemsControl ic ? ic.Items?.Count ?? 0 : 0,
            SettleMaxItems);
        return pass.AnimatedCount >= total;
    }

    private static void DetachPass(SettlePass pass)
    {
        try
        {
            pass.Host.ContainerContentChanging -= OnContainerContentChanging;
        }
        catch (Exception)
        {
            // best-effort: the host may already be torn down with the page.
        }

        if (ReferenceEquals(_pendingPass, pass))
        {
            _pendingPass = null;
        }
    }

    static partial void AbortInFlightCore()
    {
        // A superseding pass also abandons any realization-waiting pass (its containers land at rest).
        if (_pendingPass is { } pending)
        {
            DetachPass(pending);
        }

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
