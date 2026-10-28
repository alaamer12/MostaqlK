namespace MostaqlK.UI.PlatformComponents.ContentSettle;

/// <summary>
/// The v3 <c>card-settle</c> mount pass (the "components mounting" half of the morph story that
/// <c>SourceMorphTransition</c>'s radial reveal hands off to). After the feed re-points at a new
/// source — or on the page's first open — each realized project card lands with the mockup's
/// transform-only settle instead of a flat, un-transitioned repaint.
/// <para>
/// <b>Design truth</b> (verified against <c>.repertoire/design/postmvp/v3-multisource/projects.html</c>
/// <c>@keyframes card-settle</c> + <c>.settling &gt; .project-card</c>, and the README "The morph
/// mechanics (mount/demount polish)" section): <c>translateY(8px) → 0</c> over <c>.34s</c> with
/// <c>cubic-bezier(.22,.61,.36,1)</c>, a <c>50ms</c> per-card stagger, <b>transform-only</b> (the
/// mockup deliberately runs NO opacity fade — "the snapshot would freeze an invisible frame"),
/// skipped entirely under <c>prefers-reduced-motion</c>. The total is capped near <c>600ms</c>
/// regardless of item count: only the first few visible cards stagger; the rest land together.
/// </para>
/// <para>
/// <b>Mechanism (why this and not a MAUI animation):</b> <see cref="ContentSettle"/> is the exact
/// surface <c>.repertoire/.steering/v3/animation-safety.md</c> was written for. A cross-platform
/// managed translation (<c>VisualElement.Animation</c> + <c>Commit</c>, or <c>TranslateToAsync</c>,
/// or a <c>Storyboard</c> driving <c>RenderTransform</c>) allocates a native WinUI
/// <c>CompositeTransform</c> every frame — the finalizer-thread release of that churn is the
/// long-idle background-crash class (<c>0xC000027B</c>). Guardrail rule 1 bans
/// <c>TranslateTo/ScaleTo/RotateTo/FadeTo*</c>, <c>Storyboard</c>-driven transforms and
/// <c>DispatcherTimer</c> property mutation; rule 3 says a transition "must be … a single reused
/// transform via the existing mapping — never as a per-frame new-transform allocation." So on
/// Windows this drives <c>Microsoft.UI.Composition.Visual</c>.<c>Offset</c> (a plain
/// compositor <c>Vector3</c> property, NOT a XAML <c>RenderTransform</c>) with ONE
/// <c>Vector3KeyFrameAnimation</c> per card, started on the render thread. Zero managed per-frame
/// allocations — the same sanctioned compositor seam <c>SourceMorphTransition</c> uses for its
/// opacity dissolve. The whole stagger fits in the finite keyframe (a start-hold keyframe encodes
/// the delay), so there is no timer and no per-frame managed callback. Rule 2 is satisfied: every
/// pass is short (&lt;= ~600ms), finite, and abortable.
/// </para>
/// <para>
/// <b>Reduced motion / ambient-hidden / any other platform / no suitable handler path ⇒ instant, no
/// animation.</b> <see cref="MotionPreferences.IsReducedMotionRequested"/> (which already folds in
/// the hidden-to-tray state) short-circuits to a fast-finish landing at rest; the platform seam is
/// a no-op partial off Windows (no <c>.Android.cs</c>/<c>.iOS.cs</c>/<c>.MacCatalyst.cs</c>), so
/// mobile morph stays desktop-only per the mockup, exactly like <c>SourceMorphTransition</c>.
/// </para>
/// <para>
/// <b>Re-entrancy: new pass fast-finishes the in-flight one.</b> A settle started while another runs
/// immediately stops every still-animating card and lands it at <c>Translation = 0</c> (never a
/// stuck 8px offset) before starting the newest pass, so rapid source/theme spam always resolves to
/// the latest intent. This mirrors <c>SourceMorphTransition</c>'s chosen fast-finish policy
/// (documented per the contract's "pick one and document").
/// </para>
/// </summary>
public static partial class ContentSettle
{
    // ---- Mockup spec (projects.html), single source of truth for the numbers ----

    /// <summary>The card's resting dip in CSS pixels: <c>@keyframes card-settle from translateY(8px)</c>.</summary>
    public const double SettleOffsetPx = 8;

    /// <summary>Per-card travel time: <c>animation: card-settle .34s</c> = 340 ms.</summary>
    public const uint SettleDurationMs = 340;

    /// <summary><c>nth-child</c> stagger: <c>animation-delay: .05s</c> = 50 ms per visible card.</summary>
    public const uint SettleStaggerMs = 50;

    /// <summary>
    /// Wall-clock ceiling for a whole pass regardless of item count. With 340 ms travel, the stagger
    /// reaches at most <see cref="SettleMaxTotalMs"/> - <see cref="SettleDurationMs"/> = 260 ms of
    /// delay, i.e. 5 slots; card indices past that hold at the same final delay and land together.
    /// </summary>
    public const uint SettleMaxTotalMs = 600;

    /// <summary>Max number of realized cards a single pass will settle (a bound on the loop, not the visual count).</summary>
    public const int SettleMaxItems = 16;

    // cubic-bezier(.22,.61,.36,1) control points — the mockup's easing, reproduced by the compositor.
    private const float EasingX1 = 0.22f;
    private const float EasingY1 = 0.61f;
    private const float EasingX2 = 0.36f;
    private const float EasingY2 = 1.0f;

    // The active feed, held weakly so a page teardown never pins a control. The rail's morph can
    // call RunForActiveFeed() post-reveal without owning the layout's CollectionView reference.
    private static WeakReference<CollectionView>? _activeFeed;

    /// <summary>
    /// Registers <paramref name="feed"/> as the surface <see cref="RunForActiveFeed"/> settles.
    /// Called by the feed host (windows layout); harmless to call repeatedly.
    /// </summary>
    public static void RegisterActiveFeed(CollectionView? feed)
    {
        _activeFeed = feed is null ? null : new WeakReference<CollectionView>(feed);
    }

    /// <summary>
    /// Runs one settle pass over the registered feed's realized cards, if it is still alive and
    /// realized. This is the zero-argument post-reveal entrypoint <see cref="SourceMorphTransition"/>
    /// (or any other presenter) calls to land the mount after its own reveal completes.
    /// </summary>
    public static void RunForActiveFeed()
    {
        if (_activeFeed is not null && _activeFeed.TryGetTarget(out var feed))
        {
            RunFor(feed);
        }
    }

    /// <summary>
    /// Public entrypoint: runs ONE-SHOT, finite, abortable settle pass over the realized item cards
    /// of <paramref name="feed"/>. No-op (instant) when motion is reduced / the window is hidden to
    /// tray, when there is no feed, or on a platform with no compositor partial. Supersedes (and
    /// fast-finishes) any in-flight pass — a new settle always wins so a rapid switch never leaves a
    /// card parked mid-travel.
    /// </summary>
    public static void RunFor(CollectionView? feed)
    {
        if (feed is null)
        {
            return;
        }

        // Reduced motion / ambient-hidden-to-tray, or nothing to animate: land at rest instantly
        // (the mockup's prefers-reduced-motion skips the whole settle). Fast-finish first so an
        // in-flight pass on Windows cannot keep animating past this guardrail check.
        if (MotionPreferences.IsReducedMotionRequested)
        {
            AbortInFlightCore();
            return;
        }

        PerformSettle(feed);
    }

    /// <summary>
    /// Per-platform settle seam. Default (shared) has no implementation ⇒ elided on every platform
    /// except the one that provides it (Windows), where a compositor pass animates the realized
    /// cards. Everywhere else the pass is simply never started ⇒ the feed shows the new rows at rest
    /// instantly.
    /// </summary>
    static partial void PerformSettle(CollectionView feed);

    /// <summary>
    /// Per-platform fast-finish seam: stops any still-running compositor pass and lands every card
    /// it touched back at rest (<c>Translation = 0</c>). Default no-op off Windows (nothing animates
    /// there, so there is nothing to cancel).
    /// </summary>
    static partial void AbortInFlightCore();
}
