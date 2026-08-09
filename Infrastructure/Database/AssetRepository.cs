using Microsoft.Data.Sqlite;
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

    public async Task<Result<bool>> InsertAsync(Asset asset, CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = _connectionFactory.CreateConnection();
            using var command = connection.CreateCommand();
            // Metadata only - the binary content itself is written to disk by the
            // Enrichment Service; only its path/metadata is ever persisted here.
            command.CommandText = """
                INSERT INTO assets
                    (project_id, file_name, url, raw_url, local_path, size_bytes, extension, requires_auth, size_text)
                VALUES
                    (@project_id, @file_name, @url, @raw_url, @local_path, @size_bytes, @extension, @requires_auth, @size_text);
                """;
            command.Parameters.AddWithValue("@project_id", asset.ProjectId);
            command.Parameters.AddWithValue("@file_name", asset.FileName);
            command.Parameters.AddWithValue("@url", asset.Url);
            command.Parameters.AddWithValue("@raw_url", asset.RawUrl is null ? DBNull.Value : asset.RawUrl);
            command.Parameters.AddWithValue("@local_path", asset.LocalPath is null ? DBNull.Value : asset.LocalPath);
            command.Parameters.AddWithValue("@size_bytes", asset.SizeBytes is null ? DBNull.Value : asset.SizeBytes);
            command.Parameters.AddWithValue("@extension", asset.Extension is null ? DBNull.Value : asset.Extension);
            command.Parameters.AddWithValue("@requires_auth", asset.RequiresAuth ? 1 : 0);
            command.Parameters.AddWithValue("@size_text", asset.SizeText is null ? DBNull.Value : asset.SizeText);

            await command.ExecuteNonQueryAsync(cancellationToken);
            return Result<bool>.Ok(true);
        }
        catch (SqliteException ex)
        {
            return Result<bool>.Err(DatabaseErrors.QueryFailed(nameof(InsertAsync), ex));
        }
    }

    public async Task<Result<IReadOnlyList<Asset>>> GetByProjectIdAsync(long projectId, CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = _connectionFactory.CreateConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT asset_id, project_id, file_name, url, raw_url, local_path, size_bytes,
                       extension, requires_auth, size_text
                FROM assets
                WHERE project_id = @project_id;
                """;
            command.Parameters.AddWithValue("@project_id", projectId);

            var assets = new List<Asset>();
            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                assets.Add(new Asset
                {
                    AssetId = reader.GetInt64(0),
                    ProjectId = reader.GetInt64(1),
                    FileName = reader.GetString(2),
                    Url = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                    RawUrl = reader.IsDBNull(4) ? null : reader.GetString(4),
                    LocalPath = reader.IsDBNull(5) ? null : reader.GetString(5),
                    SizeBytes = reader.IsDBNull(6) ? null : reader.GetInt64(6),
                    Extension = reader.IsDBNull(7) ? null : reader.GetString(7),
                    RequiresAuth = !reader.IsDBNull(8) && reader.GetInt64(8) != 0,
                    SizeText = reader.IsDBNull(9) ? null : reader.GetString(9),
                });
            }

            return Result<IReadOnlyList<Asset>>.Ok(assets);
        }
        catch (SqliteException ex)
        {
            return Result<IReadOnlyList<Asset>>.Err(DatabaseErrors.QueryFailed(nameof(GetByProjectIdAsync), ex));
        }
    }
}
