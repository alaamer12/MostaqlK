using MostaqlK.Core;
using MostaqlK.Models;

namespace MostaqlK.Infrastructure.Database;

/// <summary>
/// Persists and queries project owners/clients in the local SQLite store.
/// </summary>
public interface IOwnerRepository
{
    Task<Result<bool>> UpsertAsync(Owner owner, CancellationToken cancellationToken = default);

    Task<Result<Owner?>> GetByIdAsync(long ownerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes only the owner rows whose <c>owner_id</c> falls within <paramref name="minOwnerId"/>
    /// ..<paramref name="maxOwnerId"/> (inclusive). Used by
    /// <c>DesignDataSeeder.PurgeSeededRowsAsync</c> to strip leftover seed owners.
    /// </summary>
    Task<Result<int>> DeleteByIdRangeAsync(long minOwnerId, long maxOwnerId, CancellationToken cancellationToken = default);
}
