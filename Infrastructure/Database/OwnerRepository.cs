using Microsoft.Data.Sqlite;
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

    [ErrorOutcome(ErrorOutcome.Handled, Label = "Query failure surfaced as Result<bool>.Err")]
    public async Task<Result<bool>> UpsertAsync(Owner owner, CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = _connectionFactory.CreateConnection();
            using var command = connection.CreateCommand();
            // Selective-update semantics: identity fields (name/profile/avatar) are only set
            // on first insert; on conflict only last_seen_at + stat columns are refreshed -
            // other owner fields are never overwritten.
            command.CommandText = """
                INSERT INTO owners
                    (owner_id, name, profile_url, avatar_url, rating, completed_projects_count,
                     hiring_rate_percent, last_seen_at)
                VALUES
                    (@owner_id, @name, @profile_url, @avatar_url, @rating, @completed_projects_count,
                     @hiring_rate_percent, @last_seen_at)
                ON CONFLICT(owner_id) DO UPDATE SET
                    last_seen_at = excluded.last_seen_at,
                    rating = excluded.rating,
                    completed_projects_count = excluded.completed_projects_count,
                    hiring_rate_percent = excluded.hiring_rate_percent;
                """;
            command.Parameters.AddWithValue("@owner_id", owner.OwnerId);
            command.Parameters.AddWithValue("@name", owner.Name);
            command.Parameters.AddWithValue("@profile_url", owner.ProfileUrl is null ? DBNull.Value : owner.ProfileUrl);
            command.Parameters.AddWithValue("@avatar_url", owner.AvatarUrl is null ? DBNull.Value : owner.AvatarUrl);
            command.Parameters.AddWithValue("@rating", owner.Rating is null ? DBNull.Value : owner.Rating);
            command.Parameters.AddWithValue("@completed_projects_count", owner.CompletedProjectsCount is null ? DBNull.Value : owner.CompletedProjectsCount);
            command.Parameters.AddWithValue("@hiring_rate_percent", owner.HiringRatePercent is null ? DBNull.Value : owner.HiringRatePercent);
            command.Parameters.AddWithValue("@last_seen_at", DateTimeOffset.UtcNow.ToString("O"));

            await command.ExecuteNonQueryAsync(cancellationToken);
            return Result<bool>.Ok(true);
        }
        catch (SqliteException ex)
        {
            return Result<bool>.Err(DatabaseErrors.QueryFailed(nameof(UpsertAsync), ex));
        }
    }

    [ErrorOutcome(ErrorOutcome.Handled, Label = "Query failure surfaced as Result.Err")]
    public async Task<Result<Owner?>> GetByIdAsync(long ownerId, CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = _connectionFactory.CreateConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT owner_id, name, profile_url, avatar_url, rating, completed_projects_count, hiring_rate_percent
                FROM owners
                WHERE owner_id = @owner_id;
                """;
            command.Parameters.AddWithValue("@owner_id", ownerId);

            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return Result<Owner?>.Ok(null);
            }

            var owner = new Owner
            {
                OwnerId = reader.GetInt64(0),
                Name = reader.GetString(1),
                ProfileUrl = reader.IsDBNull(2) ? null : reader.GetString(2),
                AvatarUrl = reader.IsDBNull(3) ? null : reader.GetString(3),
                Rating = reader.IsDBNull(4) ? null : reader.GetDouble(4),
                CompletedProjectsCount = reader.IsDBNull(5) ? null : reader.GetInt32(5),
                HiringRatePercent = reader.IsDBNull(6) ? null : reader.GetInt32(6),
            };

            return Result<Owner?>.Ok(owner);
        }
        catch (SqliteException ex)
        {
            return Result<Owner?>.Err(DatabaseErrors.QueryFailed(nameof(GetByIdAsync), ex));
        }
    }

    [ErrorOutcome(ErrorOutcome.Handled, Label = "Query failure surfaced as Result<int>.Err")]
    public async Task<Result<int>> DeleteByIdRangeAsync(long minOwnerId, long maxOwnerId, CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = _connectionFactory.CreateConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM owners WHERE owner_id BETWEEN @min_id AND @max_id;";
            command.Parameters.AddWithValue("@min_id", minOwnerId);
            command.Parameters.AddWithValue("@max_id", maxOwnerId);

            var rowsAffected = await command.ExecuteNonQueryAsync(cancellationToken);
            return Result<int>.Ok(rowsAffected);
        }
        catch (SqliteException ex)
        {
            return Result<int>.Err(DatabaseErrors.QueryFailed(nameof(DeleteByIdRangeAsync), ex));
        }
    }
}
