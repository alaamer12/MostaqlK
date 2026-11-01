namespace MostaqlK.Core.Diagnostics;

/// <summary>
/// Pure rule behind the heartbeat's finalizer-flood alarm (postmortem A3 of
/// <c>radar-iterationcount-stow-failfast-2026-10-31</c>): the metric that predicted the dev69
/// crash loop sat unread in <c>heartbeat.log</c> for nine days (<c>finalizePending</c> 6,138,
/// frozen at every death). Isolated (no GC, no IO, no timers) so the alarm's arithmetic is
/// provable against the incident's real beat numbers without manufacturing an unhealthy process.
/// <para>
/// Why a sustained backlog is the signal and a spike is not: the healthy dev70 flood test peaked
/// at 9,350 pending and drained to 214 by the next beat — a transient GC wave, harmless. The
/// dying dev69 instances held thousands across beats and never drained; that is abandoned
/// native objects finalizing off-thread, the documented fuel of the <c>0xC000027B</c> fail-fast.
/// The rule therefore compares consecutive beats, not one beat against a constant.
/// </para>
/// </summary>
public static class FinalizerFloodPolicy
{
    /// <summary>Backlog at which a beat becomes a candidate for the alarm (chosen from the incident: deaths froze at ~6,100).</summary>
    public const long Threshold = 4000;

    /// <summary>
    /// True when the PREVIOUS beat was at/above <see cref="Threshold"/> and this beat has drained
    /// less than half of it — the backlog is sustaining, not transient.
    /// </summary>
    public static bool IsSustaining(long previousPending, long currentPending) =>
        previousPending >= Threshold && currentPending > previousPending / 2;

    /// <summary>
    /// Episode latch reset with hysteresis: an alarm episode ends only once the backlog has fallen
    /// to half the threshold, so a value oscillating around 4,000 cannot re-fire every two minutes.
    /// </summary>
    public static bool EpisodeOver(long currentPending) => currentPending < Threshold / 2;
}
