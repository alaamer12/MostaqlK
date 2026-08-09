using MostaqlK.Core;
using MostaqlK.Models;

namespace MostaqlK.Infrastructure.Database;

/// <inheritdoc cref="IAssetRepository"/>
public sealed class AssetRepository : IAssetRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;

    public AssetRepository(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public Task<Result<bool>> InsertAsync(Asset asset, CancellationToken cancellationToken = default)
    {
        // TODO: INSERT INTO assets (...) using `_connectionFactory.CreateConnection()`.
        throw new NotImplementedException();
    }

    public Task<Result<IReadOnlyList<Asset>>> GetByProjectIdAsync(long projectId, CancellationToken cancellationToken = default)
    {
        // TODO: SELECT ... FROM assets WHERE project_id = @projectId.
        throw new NotImplementedException();
    }
}
