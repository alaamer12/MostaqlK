using MostaqlK.Core.Sources;

namespace MostaqlK.Services.Pipeline.DiffEngine;

/// <summary>
/// Known-state provider backed by <see cref="InFlightTracker"/> — covers projects
/// that have already been enqueued/are being enriched but not yet committed to SQLite.
/// </summary>
public sealed class InFlightSetProvider : IKnownStateProvider
{
    private readonly InFlightTracker _inFlightTracker;

    public InFlightSetProvider(InFlightTracker inFlightTracker)
    {
        _inFlightTracker = inFlightTracker;
    }

    public Task<IReadOnlySet<SourceProjectId>> GetKnownProjectIdsAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlySet<SourceProjectId> snapshot = _inFlightTracker.Snapshot().ToHashSet();
        return Task.FromResult(snapshot);
    }
}
