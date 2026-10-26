using MostaqlK.Core.Sources;

namespace MostaqlK.Services.Pipeline.DiffEngine;

/// <summary>
/// Source of "already known" projects that the diff engine checks new listing
/// results against (either the committed SQLite store or the in-memory in-flight set).
/// Keys are composite — a site-local id alone could alias another source's project.
/// </summary>
public interface IKnownStateProvider
{
    public Task<IReadOnlySet<SourceProjectId>> GetKnownProjectIdsAsync(CancellationToken cancellationToken = default);
}
