using Dapper;
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

    [ErrorOutcome(ErrorOutcome.Handled, Label = "Query failure surfaced as Result<bool>.Err")]
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

    [ErrorOutcome(ErrorOutcome.Handled, Label = "Query failure surfaced as Result.Err")]
    public async Task<Result<IReadOnlyList<Asset>>> GetByProjectIdAsync(long projectId, CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = _connectionFactory.CreateConnection();
            const string sql = """
                SELECT asset_id, project_id, file_name, url, raw_url, local_path, size_bytes,
                       extension, requires_auth, size_text
                FROM assets
                WHERE project_id = @project_id;
                """;

            var assets = new List<Asset>();
            var rows = await connection.QueryAsync<RowMaps.AssetRow>(
                new CommandDefinition(sql, new { project_id = projectId }, cancellationToken: cancellationToken));
            foreach (var row in rows)
            {
                assets.Add(row.ToAsset());
            }

            return Result<IReadOnlyList<Asset>>.Ok(assets);
        }
        catch (SqliteException ex)
        {
            return Result<IReadOnlyList<Asset>>.Err(DatabaseErrors.QueryFailed(nameof(GetByProjectIdAsync), ex));
        }
    }
}
