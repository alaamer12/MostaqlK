using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using MostaqlK.Services.Diagnostics;
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
                        catch (Exception ex)
                        {
                            InteractionLogger.Fault("ContentSettle.Pass.Apply", ex);
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
        catch (Exception ex)
        {
            // A decorative mount pass must never take the app down; land at rest and move on.
            InteractionLogger.Fault("ContentSettle.Run", ex);
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
        catch (Exception ex)
        {
            InteractionLogger.Fault("ContentSettle.ContainerAnimate", ex);
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
        catch (Exception ex)
        {
            // best-effort: the host may already be torn down with the page.
            InteractionLogger.Fault("ContentSettle.Detach.Event", ex);
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
            catch (Exception ex)
            {
                // best-effort release; a visual may already be torn down with its container.
                InteractionLogger.Fault("ContentSettle.Visual.StopAnimation", ex);
            }
        }

        _runningVisuals.Clear();
    }

    /// <summary>
    /// Starts the one-shot dip-and-rise on a single realized card container, using the cached
    /// per-stagger-slot animation (<see cref="EnsureAnimationCache"/>).
    /// </summary>
    private static void AnimateContainer(WinUIElement container, int index)
    {
        WinUIVisual visual = ElementCompositionPreview.GetElementVisual(container);
        Compositor compositor = visual.Compositor;

        int slot = Math.Min(index, MaxStaggerSlots);
        EnsureAnimationCache(compositor);
        var animation = _slotAnimations![slot];

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

    private static Compositor? _cacheCompositor;
    private static Vector3KeyFrameAnimation[]? _slotAnimations;

    /// <summary>
    /// Builds (once per compositor) the <see cref="MaxStaggerSlots"/> + 1 animations the stagger needs
    /// - one per delay slot - and holds them for reuse.
    /// <para>
    /// <b>Why cached:</b> this unit used to create a <c>Vector3KeyFrameAnimation</c> and a
    /// <c>CubicBezierEasingFunction</c> per card per pass - 32 composition objects per settle, and a
    /// pass runs on every structural feed patch, so an enrichment burst minted hundreds. Composition
    /// objects are native peers whose release lands on the finalizer thread when the GC collects them,
    /// which is the fuel named by <c>docs/postmoterm/published-layout-dispatcher-failfast-2026-10-28.md</c>
    /// §10/§11 ("reduce the churn, not the handling"; <c>finalizePending</c> in the thousands within
    /// seconds of launch). A keyframe animation is an immutable template that any number of visuals can
    /// start from the same compositor, so one object per stagger slot replaces all of it. The easing
    /// function is folded into the animations and never re-created.
    /// </para>
    /// </summary>
    private static void EnsureAnimationCache(Compositor compositor)
    {
        if (_slotAnimations is not null && ReferenceEquals(_cacheCompositor, compositor))
        {
            return;
        }

        // A different compositor means the old objects belong to a dead device (window torn down).
        // Drop them here, on the UI thread, instead of letting the finalizer do it later.
        DisposeAnimationCache();

        var ease = compositor.CreateCubicBezierEasingFunction(
            new Vector2(EasingX1, EasingY1), new Vector2(EasingX2, EasingY2));

        var slots = new Vector3KeyFrameAnimation[MaxStaggerSlots + 1];
        for (int slot = 0; slot <= MaxStaggerSlots; slot++)
        {
            int delayMs = slot * (int)SettleStaggerMs;
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
            animation.InsertKeyFrame(1f, Vector3.Zero, ease);
            slots[slot] = animation;
        }

        _slotAnimations = slots;
        _cacheCompositor = compositor;
    }

    private static void DisposeAnimationCache()
    {
        if (_slotAnimations is null)
        {
            return;
        }

        foreach (var animation in _slotAnimations)
        {
            try
            {
                animation?.Dispose();
            }
            catch (Exception ex)
            {
                InteractionLogger.Fault("ContentSettle.AnimationCache.Dispose", ex);
            }
        }

        _slotAnimations = null;
        _cacheCompositor = null;
    }

    /// <summary>
    /// FEED BUG HUNT (dev74): native-side probe. Logs how many items the WinUI items host holds,
    /// how many containers the virtualizer actually realized, each container's visibility/opacity/
    /// height, and the scroll extents. Rows present in data but absent here ⇒ renderer collapse;
    /// one item here ⇒ the collapse arrived from the store. Remove with the hunt.
    /// </summary>
    static partial void ProbeFeed(CollectionView feed, string tag)
    {
        if (feed.Handler?.PlatformView is not WinUIDependencyObject platformRoot)
        {
            MostaqlK.Services.Diagnostics.FeedTrace.Diag("FeedDiag.NativeList", tag,
                new { reason = "feed.Handler?.PlatformView is null" });
            return;
        }

        if (ResolveItemsHost(platformRoot) is not WinUIListViewBase host)
        {
            MostaqlK.Services.Diagnostics.FeedTrace.Diag("FeedDiag.NativeList", tag,
                new { reason = "items host not found in visual tree" });
            return;
        }

        int items = host is Microsoft.UI.Xaml.Controls.ItemsControl ic ? ic.Items?.Count ?? 0 : -1;
        int realized = 0;
        int visible = 0;
        int inViewport = 0;

        // The scroll extents are what the user actually sees: 40 items with a viewport that cannot
        // scroll past one card IS the "only one project left" symptom, reported by the list itself.

        var scroll = FindScrollViewer(host);
        double viewportGuess = scroll?.ViewportHeight is > 0 ? scroll.ViewportHeight : 1;
        var sample = new List<string>();
        var ys = new List<double>();
        var conts = new List<int>();
        int probe = Math.Min(items, 12);
        for (int i = 0; i < probe; i++)
        {
            if (host.ContainerFromIndex(i) is WinUIElement container)
            {
                realized++;
                bool onScreen = container.Visibility == Microsoft.UI.Xaml.Visibility.Visible;
                if (onScreen)
                {
                    visible++;
                }

                // FEED BUG HUNT (dev76): the position, not just the existence. A container can be
                // realized, Visible, 322 px tall and STILL not be where the user is looking - stacked
                // on top of its neighbour, parked above the viewport, or clipped - which is exactly
                // "the cards are gone but the list says it has them". TransformToVisual throws when a
                // container is mid-recycle, so each probe is defensive.
                double y = double.NaN;
                double x = double.NaN;
                try
                {
                    if (container is Microsoft.UI.Xaml.FrameworkElement fe &&
                        fe.TransformToVisual(host) is Microsoft.UI.Xaml.Media.GeneralTransform gt)
                    {
                        var point = gt.TransformPoint(new Windows.Foundation.Point(0, 0));
                        x = Math.Round(point.X, 1);
                        y = Math.Round(point.Y, 1);
                    }
                }
                catch (Exception probeEx)
                {
                    _ = probeEx;
                    // swallow-ok: diagnostic probe — a container not connected yet answers with NaN
                    // by design; the read failing IS the sample, not a fault to report.
                }

                double height = container is Microsoft.UI.Xaml.FrameworkElement hfe2 ? hfe2.ActualHeight : -1;
                double width = container is Microsoft.UI.Xaml.FrameworkElement hfe3 ? hfe3.ActualWidth : -1;

                // FEED BUG HUNT (dev77): twelve containers all reporting y=0 is either a genuinely
                // stacked panel or a set of DETACHED stale references (a disconnected element
                // transforms as identity). IsLoaded + the parent chain is what separates them, and
                // the root-relative point says where the user is actually seeing it.
                bool loaded = false;
                string parent = "?";
                double rootY = double.NaN;
                try
                {
                    if (container is Microsoft.UI.Xaml.FrameworkElement feLoaded)
                    {
                        loaded = feLoaded.IsLoaded;
                    }

                    if (WinUIVisualTreeHelper.GetParent(container) is WinUIDependencyObject p)
                    {
                        parent = p.GetType().Name;
                    }

                    if (container is Microsoft.UI.Xaml.FrameworkElement feRoot &&
                        feRoot.TransformToVisual(null) is Microsoft.UI.Xaml.Media.GeneralTransform gtRoot)
                    {
                        rootY = Math.Round(gtRoot.TransformPoint(new Windows.Foundation.Point(0, 0)).Y, 1);
                    }
                }
                catch (Exception probeEx)
                {
                    _ = probeEx;
                    // swallow-ok: diagnostic probe — detached mid-read is itself the answer the
                    // feed-hunt sample is looking for; reporting it would be noise, not evidence.
                }

                bool withinViewport = !double.IsNaN(y) && y > -height && y < viewportGuess;
                if (withinViewport)
                {
                    inViewport++;
                }

                // The card's own first visual child answers "realized but painted empty".
                string inner = "?";
                try
                {
                    if (WinUIVisualTreeHelper.GetChildrenCount(container) > 0 &&
                        WinUIVisualTreeHelper.GetChild(container, 0) is Microsoft.UI.Xaml.FrameworkElement child)
                    {
                        inner = $"{Math.Round(child.ActualWidth, 0)}x{Math.Round(child.ActualHeight, 0)}";
                    }
                }
                catch (Exception probeEx)
                {
                    _ = probeEx;
                    // swallow-ok: diagnostic probe — recycled mid-read; the emptiness is the data.
                }

                ys.Add(y);
                // dev86: container IDENTITY. "nine containers all at y=0" and "the same container
                // handed back nine times" look identical in every field above and are completely
                // different bugs - one is a placement failure, the other a corrupted index map.
                conts.Add(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(container));
                sample.Add($"{i}:y={y},root={rootY},x={x},c#{System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(container) % 100000},{container.Visibility},{Math.Round(container.Opacity, 2)},{Math.Round(width, 0)}x{Math.Round(height, 0)},load:{(loaded ? 1 : 0)},par:{parent},in:{inner}");
            }
            else
            {
                sample.Add($"{i}:NOT-REALIZED");
            }
        }

        double viewport = scroll?.ViewportHeight ?? -1;
        double extent = scroll?.ScrollableHeight ?? -1;

        // FEED BUG HUNT (dev74/dev76/dev77): the verdict line, so the hunt is one grep away. Three
        // ways to be stuck: the panel realized almost nothing; it realized plenty but at most one
        // card lands inside the viewport; or — the owner's case — every realized container reports
        // the SAME y, i.e. the cards are stacked on one another and the feed shows a single project
        // while the data holds 76.
        int distinctY = ys.Where(v => !double.IsNaN(v)).Select(v => (int)Math.Round(v / 8)).Distinct().Count();
        bool stacked = realized >= 3 && distinctY <= 2;
        bool stuck = items > 3 && (realized <= 1 || inViewport <= 1 || stacked
            || (viewport > 0 && extent > 0 && extent < viewport * 1.6));
        if (stuck)
        {
            MostaqlK.Services.Diagnostics.FeedTrace.Diag("FeedDiag.Collapse", tag, new
            {
                nativeItems = items,
                realized,
                visible,
                inViewport,
                stacked,
                distinctY,
                distinctContainers = conts.Distinct().Count(),
                panelChildren = FindItemsPanel(host) is { } pnl ? pnl.Children.Count : -1,
                viewport = Math.Round(viewport, 1),
                scrollableExtent = Math.Round(extent, 1),
                scrollOffset = scroll is null ? -1 : Math.Round(scroll.VerticalOffset, 1),
                hostVisibility = host.Visibility.ToString(),
                feedIsVisible = feed.IsVisible,
                feedOpacity = feed.Opacity,
                sample,
            });
        }

        MostaqlK.Services.Diagnostics.FeedTrace.Diag("FeedDiag.NativeList", tag, new
        {
            nativeItems = items,
            realized,
            visible,
            inViewport,
            stacked,
            distinctY,
            distinctContainers = conts.Distinct().Count(),
            panelChildren = FindItemsPanel(host) is { } pnl2 ? pnl2.Children.Count : -1,
            stuck,
            hostVisibility = host.Visibility.ToString(),
            hostHeight = Math.Round(host is Microsoft.UI.Xaml.FrameworkElement hfe ? hfe.ActualHeight : -1, 1),
            feedHeight = Math.Round(feed.Height, 1),
            scrollableViewport = Math.Round(viewport, 1),
            scrollableExtent = Math.Round(extent, 1),
            scrollOffset = scroll is null ? -1 : Math.Round(scroll.VerticalOffset, 1),
            sample,
        });
    }

    /// <summary>FEED BUG FIX (dev81): the panel that owns the container positions.</summary>
    private static Microsoft.UI.Xaml.Controls.Panel? FindItemsPanel(WinUIDependencyObject root, int depth = 0)
    {
        if (depth > 8)
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

            if (childDo is Microsoft.UI.Xaml.Controls.Panel panel)
            {
                return panel;
            }

            if (FindItemsPanel(childDo, depth + 1) is { } found)
            {
                return found;
            }
        }

        return null;
    }


    // dev80 trace: the container witness. Bounded (a scroll generates one event per item per pass)
    // and it references no feature types: the item is reported by runtime type + identity hash, which
    // the FeedTrace.Patch lines already map to project ids by index.
    private static WinUIListViewBase? _watchedHost;
    private static int _containerWatchEvents;
    private const int ContainerWatchCap = 900;

    static partial void WatchContainersFeed(CollectionView feed)
    {
        if (feed.Handler?.PlatformView is not WinUIDependencyObject root
            || ResolveItemsHost(root) is not WinUIListViewBase host)
        {
            return;
        }

        if (ReferenceEquals(_watchedHost, host))
        {
            return; // already watching this host
        }

        if (_watchedHost is not null)
        {
            _watchedHost.ContainerContentChanging -= OnContainerWatch;
        }

        _watchedHost = host;
        host.ContainerContentChanging += OnContainerWatch;
    }

    private static void OnContainerWatch(WinUIListViewBase sender, Microsoft.UI.Xaml.Controls.ContainerContentChangingEventArgs args)
    {
        if (_containerWatchEvents >= ContainerWatchCap)
        {
            return;
        }

        _containerWatchEvents++;
        var item = args.Item;
        MostaqlK.Services.Diagnostics.FeedTrace.Log("Container", args.InRecycleQueue ? "recycle" : "bind", new
        {
            idx = args.ItemIndex,
            item = item is null ? "null" : item.GetType().Name + "#" + System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(item),
            phase = args.Phase.ToString(),
            cont = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(sender.ContainerFromIndex(args.ItemIndex) ?? sender),
            ev = _containerWatchEvents,
        });
    }


    /// <summary>FEED BUG HUNT (dev87): enumerate every items host in the feed's subtree.</summary>
    private static void CountHosts(WinUIDependencyObject root, int depth = 0)
    {
        if (depth > 14)
        {
            return;
        }

        int children = WinUIVisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < children; i++)
        {
            if (WinUIVisualTreeHelper.GetChild(root, i) is WinUIDependencyObject childDo)
            {
                DescribeHost(childDo, depth);
                CountHosts(childDo, depth + 1);
            }
        }
    }
    /// <summary>FEED BUG HUNT (dev87): record one items host found in the tree.</summary>
    private static void DescribeHost(WinUIDependencyObject childDo, int depth)
    {
        if (childDo is WinUIListViewBase)
        {
            // dev87: a second items host under the same window is the signature of a duplicated
            // CollectionView after navigation, which would explain containers that exist, are
            // loaded, and are still not laid out by the panel the screen is showing.
            var lb = (Microsoft.UI.Xaml.FrameworkElement)childDo;
            MostaqlK.Services.Diagnostics.FeedTrace.Log("Hosts", "listviewbase", new
            {
                depth,
                w = Math.Round(lb.ActualWidth, 0),
                h = Math.Round(lb.ActualHeight, 0),
                children = WinUIVisualTreeHelper.GetChildrenCount(childDo),
                items = lb is Microsoft.UI.Xaml.Controls.ItemsControl lic ? lic.Items?.Count ?? -1 : -1,
                visible = lb.Visibility.ToString(),
            });
        }

        if (childDo is Microsoft.UI.Xaml.Controls.ItemsPresenter)
        {
            MostaqlK.Services.Diagnostics.FeedTrace.Log("Hosts", "itemsPresenter", new
            {
                depth,
                children = WinUIVisualTreeHelper.GetChildrenCount(childDo),
            });
        }
    }

    /// <summary>FEED BUG HUNT (dev74): one-shot visual-tree search for the list's ScrollViewer.</summary>
    private static Microsoft.UI.Xaml.Controls.ScrollViewer? FindScrollViewer(WinUIDependencyObject root, int depth = 0)
    {
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

            if (childDo is Microsoft.UI.Xaml.Controls.ScrollViewer sv)
            {
                return sv;
            }

            if (FindScrollViewer(childDo, depth + 1) is { } found)
            {
                return found;
            }
        }

        return null;
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
