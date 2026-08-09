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
}
