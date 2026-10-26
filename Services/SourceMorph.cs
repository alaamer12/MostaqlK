using MostaqlK.Core.Sources;
using MostaqlK.UI.DesignSystem.BrandTokens;

namespace MostaqlK.Services;

/// <summary>
/// Performs the permanent source morph — the single canonical triple: persist
/// <see cref="SettingsKeys.ActiveSource"/>, re-point the status projection (which re-targets every
/// live pipeline consumer resolved through <c>ActivePipeline</c>), then swap the brand palette
/// (DynamicResource repaint). Both callers share it: the rail tile tap (<c>SourceSwitchTiles</c>)
/// and the Windows toast-activation path, where clicking another source's project notification
/// permanently morphs to that source before navigating (contract §6, owner decision). Kept in
/// cross-platform code so every build compiles it, not only the Windows target.
/// </summary>
/// <remarks>
/// <b>Two entry points, one mutation (dev30 click-path):</b>
/// <list type="bullet">
/// <item><see cref="Apply"/> — the canonical synchronous triple for NON-interactive callers (toast
/// activation, startup restore): the preference write lands before the method returns.</item>
/// <item><see cref="ApplyOptimistic"/> — the interactive rail click path: the in-memory, paint-state
/// ops (projection + palette) run synchronously so the reveal sees the new state in its own frame,
/// while the disk write is queued fire-and-forget through <see cref="DeferSet"/> (ordered,
/// last-wins). A fast local file is still a slow source (tail latency) — the click frame must never
/// await it.</item>
/// </list>
/// </remarks>
public static class SourceMorph
{
    public static void Apply(
        ISettingsRepository? settings,
        GlobalAppStatusService? globalStatus,
        IBrandTokenProvider? tokenProvider,
        SourceId source)
    {
        if (tokenProvider?.CurrentSource == source)
        {
            return;
        }

        settings?.SetGlobal(SettingsKeys.ActiveSource, source.Value);
        globalStatus?.SetActiveProjection(source);
        tokenProvider?.SwapTo(source);
    }

    /// <summary>
    /// The optimistic click-path triple (<c>SourceSwitchTiles</c> ONLY — every other caller keeps
    /// <see cref="Apply"/>). Synchronous: <see cref="GlobalAppStatusService.SetActiveProjection"/> +
    /// <see cref="IBrandTokenProvider.SwapTo"/> (both are cheap in-memory ops; the palette repaint
    /// IS the optimistic paint). Deferred (fire-and-forget, ordered last-wins): the
    /// <see cref="SettingsKeys.ActiveSource"/> persistence, which otherwise writes
    /// <c>preferences.dat</c> synchronously on the UI thread through
    /// <see cref="MostaqlK.Core.Platform.FilePreferences"/>.
    /// </summary>
    public static void ApplyOptimistic(
        ISettingsRepository? settings,
        GlobalAppStatusService? globalStatus,
        IBrandTokenProvider? tokenProvider,
        SourceId source)
    {
        if (tokenProvider?.CurrentSource == source)
        {
            return;
        }

        DeferSet(settings, SettingsKeys.ActiveSource, source.Value);
        globalStatus?.SetActiveProjection(source);
        tokenProvider?.SwapTo(source);
    }

    // ------------------------------------------------------------------ deferred preference writes

    // MERGE-POINT(dev30, parent): the feed-store agent owns a `DeferredPreferences` helper under
    // Core/Platform. It did not exist in the tree when this queue was written, so the rail/theme
    // click path uses this minimal ordered last-wins queue instead. When both waves converge, the
    // parent should either route DeferSet/FlushDeferredPersistence through DeferredPreferences or
    // fold this queue into it — keep THIS the single API surface callers use either way.

    private static readonly object _deferGate = new();

    /// <summary>Pending writes keyed by preference key; a newer write for the same key replaces
    /// (never queues behind) the older one — last-wins.</summary>
    private static readonly Dictionary<string, PendingWrite> _deferred = [];

    private static long _deferSequence;
    private static Task? _deferDrain;

    private sealed class PendingWrite
    {
        public required long Sequence { get; init; }
        public required Action Apply { get; init; }
    }

    /// <summary>
    /// Queues a global preference write off the calling (UI) thread. Ordering: each enqueue carries
    /// a monotonic sequence; the single background drainer applies pending writes in sequence order,
    /// so interleaved writes to DIFFERENT keys land in the order they were requested while writes to
    /// the SAME key collapse to the newest value. Best-effort: a throwing repository never surfaces
    /// into the click frame.
    /// </summary>
    public static void DeferSet<T>(ISettingsRepository? settings, string key, T value)
    {
        if (settings is null)
        {
            return;
        }

        lock (_deferGate)
        {
            _deferred[key] = new PendingWrite
            {
                Sequence = ++_deferSequence,
                Apply = () => settings.SetGlobal(key, value),
            };

            _deferDrain ??= Task.Run(DrainDeferred);
        }
    }

    private static void DrainDeferred()
    {
        while (true)
        {
            PendingWrite next;
            lock (_deferGate)
            {
                if (_deferred.Count == 0)
                {
                    _deferDrain = null;
                    return;
                }

                string bestKey = string.Empty;
                var bestSequence = long.MaxValue;
                foreach (var (key, pending) in _deferred)
                {
                    if (pending.Sequence < bestSequence)
                    {
                        bestSequence = pending.Sequence;
                        bestKey = key;
                    }
                }

                next = _deferred[bestKey];
                _deferred.Remove(bestKey);
            }

            try
            {
                next.Apply();
            }
            catch (Exception)
            {
                // Persistence is best-effort off the UI thread; a failed disk write must never
                // travel back into a click frame. (FilePreferences itself already swallows IO.)
            }
        }
    }

    /// <summary>
    /// Bounded wait for the deferred queue to drain — call from the window-closing hook so a quit
    /// with a pending morph/theme write does not lose it (restart-restore must see the truth).
    /// Never throws; a timeout simply leaves the remaining writes unpersisted (best-effort).
    /// </summary>
    public static void FlushDeferredPersistence(TimeSpan timeout)
    {
        Task? drain;
        lock (_deferGate)
        {
            drain = _deferDrain;
        }

        try
        {
            drain?.Wait(timeout);
        }
        catch (Exception)
        {
            // shutdown hook: never block app teardown on a stuck persistence write
        }
    }
}
