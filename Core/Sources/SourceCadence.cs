namespace MostaqlK.Core.Sources;

/// <summary>
/// Cadence facts that are shared between the settings catalog and the engine, so the number a row
/// advertises and the number a pipeline runs cannot drift apart.
/// <para>
/// The scan interval is a SYSTEM value (one setting for every platform) while the tolerance for being
/// polled is a fact about ONE site, so the effective cadence is the shared value clamped UP to the
/// site's floor: <see cref="ISourceDescriptor.MinSafePollIntervalSeconds"/>. Every consumer — the
/// pipeline start-up, the live settings apply, and the feed header caption — goes through
/// <see cref="ClampPollInterval"/>, because three copies of one clamp is three chances to lie.
/// </para>
/// </summary>
public static class SourceCadence
{
    /// <summary>Ceiling the poll loop never exceeds, whatever is stored (the settings row validates the same band).</summary>
    public const int MaxPollIntervalSeconds = 3600;

    /// <summary>Untouched-install cadence; the system row's default literal is this same number.</summary>
    public const int DefaultPollIntervalSeconds = 30;

    /// <summary>Untouched-install detail-fetch concurrency; the system row's default literal is this same number.</summary>
    public const int DefaultParallelDetailFetches = 2;

    public static int ClampPollInterval(this ISourceDescriptor descriptor, int requestedSeconds) =>
        Math.Clamp(requestedSeconds, descriptor.MinSafePollIntervalSeconds, MaxPollIntervalSeconds);
}
