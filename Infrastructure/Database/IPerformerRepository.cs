using MostaqlK.Core;
using MostaqlK.Core.Sources;
using MostaqlK.Models;

namespace MostaqlK.Infrastructure.Database;

/// <summary>
/// Competitor/rival performer files, keyed by the site-local stable key (R3). Every operation is
/// scoped to a single source and never merges across sources (v4 §1 isolation rules I2/I4): the
/// storage key is the composite <c>(source_id, performer_key)</c>, so a Mostaql username and a
/// Nafezly numeric id cannot alias each other even if their raw strings happened to match.
/// </summary>
public interface IPerformerRepository
{
    public Task<Result<bool>> UpsertAsync(Performer performer, CancellationToken cancellationToken = default);

    public Task<Result<Performer?>> GetByKeyAsync(SourceId source, string performerKey, CancellationToken cancellationToken = default);

    public Task<Result<IReadOnlyList<Performer>>> ListBySourceAsync(SourceId source, CancellationToken cancellationToken = default);
}
