namespace MostaqlK.Services.Pipeline.DiffEngine;

/// <summary>
/// Source of "already known" project IDs that the diff engine checks new listing
/// results against (either the committed SQLite store or the in-memory in-flight set).
/// </summary>
public interface IKnownStateProvider
{
    Task<IReadOnlySet<long>> GetKnownProjectIdsAsync(CancellationToken cancellationToken = default);
}
