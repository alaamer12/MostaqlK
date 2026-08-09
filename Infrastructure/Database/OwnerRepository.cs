using MostaqlK.Core;
using MostaqlK.Models;

namespace MostaqlK.Infrastructure.Database;

/// <inheritdoc cref="IOwnerRepository"/>
public sealed class OwnerRepository : IOwnerRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;

    public OwnerRepository(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public Task<Result<bool>> UpsertAsync(Owner owner, CancellationToken cancellationToken = default)
    {
        // TODO: INSERT OR REPLACE INTO owners (...) using `_connectionFactory.CreateConnection()`.
        throw new NotImplementedException();
    }

    public Task<Result<Owner?>> GetByIdAsync(long ownerId, CancellationToken cancellationToken = default)
    {
        // TODO: SELECT ... FROM owners WHERE owner_id = @ownerId.
        throw new NotImplementedException();
    }
}
