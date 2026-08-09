using MostaqlK.Core;
using MostaqlK.Models;

namespace MostaqlK.Infrastructure.Database;

/// <summary>
/// Persists and queries project attachments/assets in the local SQLite store.
/// </summary>
public interface IAssetRepository
{
    Task<Result<bool>> InsertAsync(Asset asset, CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<Asset>>> GetByProjectIdAsync(long projectId, CancellationToken cancellationToken = default);
}
