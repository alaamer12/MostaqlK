using System.Collections.Concurrent;
using MostaqlK.Core.Sources;

namespace MostaqlK.Services.Pipeline;

/// <summary>
/// Tracks projects that are currently being enriched, so the same project is never
/// enqueued twice while it is still in-flight. The SQLite-committed store is the
/// permanent backstop; this tracker only covers the in-memory window.
/// </summary>
public sealed class InFlightTracker
{
    private readonly ConcurrentDictionary<SourceProjectId, byte> _inFlight = new();

    public bool TryMarkInFlight(SourceProjectId projectId) => _inFlight.TryAdd(projectId, 0);

    public void MarkComplete(SourceProjectId projectId) => _inFlight.TryRemove(projectId, out _);

    public bool IsInFlight(SourceProjectId projectId) => _inFlight.ContainsKey(projectId);

    public IReadOnlyCollection<SourceProjectId> Snapshot() => _inFlight.Keys.ToList();
}
