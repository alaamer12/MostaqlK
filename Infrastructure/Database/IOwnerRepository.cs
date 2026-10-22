using MostaqlK.Core;
using MostaqlK.Core.Sources;
using MostaqlK.Models;

namespace MostaqlK.Infrastructure.Database;

/// <summary>
/// Persists and queries project owners/clients in the local SQLite store.
/// Owner ids are site-local (some are username hashes), so every lookup carries its source.
/// </summary>
public interface IOwnerRepository
{
    public Task<Result<bool>> UpsertAsync(Owner owner, CancellationToken cancellationToken = default);

    public Task<Result<Owner?>> GetByIdAsync(SourceId source, long ownerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes only the owner rows of one source whose <em>site</em> id falls within
    /// <paramref name="minOwnerId"/>..<paramref name="maxOwnerId"/> (inclusive). Used by
    /// <c>DesignDataSeeder.PurgeSeededRowsAsync</c> to strip leftover seed owners.
    /// </summary>
    public Task<Result<int>> DeleteByIdRangeAsync(SourceId source, long minOwnerId, long maxOwnerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches all owners for debugging/export.
    /// </summary>
    public Task<Result<IReadOnlyList<Owner>>> GetAllAsync(CancellationToken cancellationToken = default);
}
