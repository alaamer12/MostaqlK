using Dapper;
using Microsoft.Data.Sqlite;
using MostaqlK.Core;
using MostaqlK.Core.Sources;
using MostaqlK.Models;

namespace MostaqlK.Infrastructure.Database;

/// <inheritdoc cref="IOwnerRepository"/>
public sealed class OwnerRepository : IOwnerRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly SourceRegistry _registry;

    public OwnerRepository(SqliteConnectionFactory connectionFactory, SourceRegistry registry)
    {
        _connectionFactory = connectionFactory;
        _registry = registry;
    }

    private long InternalId(Owner owner) => _registry.ToInternalId(new SourceProjectId(owner.Source, owner.OwnerId));

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
                    (owner_id, source_id, name, profile_url, avatar_url, rating, completed_projects_count,
                     hiring_rate_percent, registered_at, open_projects_count, 
                     in_progress_projects_count, ongoing_communications_count, last_seen_at)
                VALUES
                    (@owner_id, @source_id, @name, @profile_url, @avatar_url, @rating, @completed_projects_count,
                     @hiring_rate_percent, @registered_at, @open_projects_count, 
                     @in_progress_projects_count, @ongoing_communications_count, @last_seen_at)
                ON CONFLICT(owner_id) DO UPDATE SET
                    last_seen_at = excluded.last_seen_at,
                    rating = excluded.rating,
                    completed_projects_count = excluded.completed_projects_count,
                    hiring_rate_percent = excluded.hiring_rate_percent,
                    registered_at = excluded.registered_at,
                    open_projects_count = excluded.open_projects_count,
                    in_progress_projects_count = excluded.in_progress_projects_count,
                    ongoing_communications_count = excluded.ongoing_communications_count;
                """;
            command.Parameters.AddWithValue("@owner_id", InternalId(owner));
            command.Parameters.AddWithValue("@source_id", owner.Source.Value);
            command.Parameters.AddWithValue("@name", owner.Name);
            command.Parameters.AddWithValue("@profile_url", owner.ProfileUrl is null ? DBNull.Value : owner.ProfileUrl);
            command.Parameters.AddWithValue("@avatar_url", owner.AvatarUrl is null ? DBNull.Value : owner.AvatarUrl);
            command.Parameters.AddWithValue("@rating", owner.Rating is null ? DBNull.Value : owner.Rating);
            command.Parameters.AddWithValue("@completed_projects_count", owner.CompletedProjectsCount is null ? DBNull.Value : owner.CompletedProjectsCount);
            command.Parameters.AddWithValue("@hiring_rate_percent", owner.HiringRatePercent is null ? DBNull.Value : owner.HiringRatePercent);
            command.Parameters.AddWithValue("@registered_at", owner.RegisteredAt is null ? DBNull.Value : owner.RegisteredAt);
            command.Parameters.AddWithValue("@open_projects_count", owner.OpenProjectsCount is null ? DBNull.Value : owner.OpenProjectsCount);
            command.Parameters.AddWithValue("@in_progress_projects_count", owner.InProgressProjectsCount is null ? DBNull.Value : owner.InProgressProjectsCount);
            command.Parameters.AddWithValue("@ongoing_communications_count", owner.OngoingCommunicationsCount is null ? DBNull.Value : owner.OngoingCommunicationsCount);
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
    public async Task<Result<Owner?>> GetByIdAsync(SourceId source, long ownerId, CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = _connectionFactory.CreateConnection();
            const string sql = """
                SELECT owner_id, source_id, name, profile_url, avatar_url, rating, completed_projects_count, hiring_rate_percent,
                       registered_at, open_projects_count, in_progress_projects_count, ongoing_communications_count
                FROM owners
                WHERE owner_id = @owner_id;
                """;

            var row = await connection.QueryFirstOrDefaultAsync<RowMaps.OwnerRow>(
                new CommandDefinition(sql, new { owner_id = _registry.ToInternalId(new SourceProjectId(source, ownerId)) },
                    cancellationToken: cancellationToken));
            if (row is null)
            {
                return Result<Owner?>.Ok(null);
            }

            return Result<Owner?>.Ok(row.ToOwner(_registry));
        }
        catch (SqliteException ex)
        {
            return Result<Owner?>.Err(DatabaseErrors.QueryFailed(nameof(GetByIdAsync), ex));
        }
    }

    [ErrorOutcome(ErrorOutcome.Handled, Label = "Query failure surfaced as Result<int>.Err")]
    public async Task<Result<int>> DeleteByIdRangeAsync(SourceId source, long minOwnerId, long maxOwnerId, CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = _connectionFactory.CreateConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM owners WHERE source_id = @source AND owner_id BETWEEN @min_id AND @max_id;";
            command.Parameters.AddWithValue("@source", source.Value);
            command.Parameters.AddWithValue("@min_id", _registry.ToInternalId(new SourceProjectId(source, minOwnerId)));
            command.Parameters.AddWithValue("@max_id", _registry.ToInternalId(new SourceProjectId(source, maxOwnerId)));

            var rowsAffected = await command.ExecuteNonQueryAsync(cancellationToken);
            return Result<int>.Ok(rowsAffected);
        }
        catch (SqliteException ex)
        {
            return Result<int>.Err(DatabaseErrors.QueryFailed(nameof(DeleteByIdRangeAsync), ex));
        }
    }

    [ErrorOutcome(ErrorOutcome.Handled, Label = "Query failure surfaced as Result.Err")]
    public async Task<Result<IReadOnlyList<Owner>>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = _connectionFactory.CreateConnection();
            const string sql = """
                SELECT owner_id, source_id, name, profile_url, avatar_url, rating, completed_projects_count, hiring_rate_percent,
                       registered_at, open_projects_count, in_progress_projects_count, ongoing_communications_count
                FROM owners;
                """;

            var results = new List<Owner>();
            var rows = await connection.QueryAsync<RowMaps.OwnerRow>(
                new CommandDefinition(sql, cancellationToken: cancellationToken));
            foreach (var row in rows)
            {
                results.Add(row.ToOwner(_registry));
            }

            return Result<IReadOnlyList<Owner>>.Ok(results);
        }
        catch (SqliteException ex)
        {
            return Result<IReadOnlyList<Owner>>.Err(DatabaseErrors.QueryFailed(nameof(GetAllAsync), ex));
        }
    }
}
