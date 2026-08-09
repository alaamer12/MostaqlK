using System.Collections.Concurrent;

namespace MostaqlK.Services.Pipeline;

/// <summary>
/// Tracks project IDs that are currently being enriched, so the same ID is never
/// enqueued twice while it is still in-flight. The SQLite-committed store is the
/// permanent backstop; this tracker only covers the in-memory window.
/// </summary>
public sealed class InFlightTracker
{
    private readonly ConcurrentDictionary<long, byte> _inFlight = new();

    public bool TryMarkInFlight(long projectId) => _inFlight.TryAdd(projectId, 0);

    public void MarkComplete(long projectId) => _inFlight.TryRemove(projectId, out _);

    public bool IsInFlight(long projectId) => _inFlight.ContainsKey(projectId);

    public IReadOnlyCollection<long> Snapshot() => _inFlight.Keys.ToList();
}
