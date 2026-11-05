using Dapper;
using Microsoft.Data.Sqlite;
using MostaqlK.Core;
using MostaqlK.Core.Sources;
using MostaqlK.Models;

namespace MostaqlK.Infrastructure.Database;

/// <inheritdoc cref="IPerformerRepository"/>
public sealed class PerformerRepository : IPerformerRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;

    public PerformerRepository(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    [ErrorOutcome(ErrorOutcome.Handled, Label = "Upsert failure surfaced as Result<bool>.Err")]
    public async Task<Result<bool>> UpsertAsync(Performer performer, CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = _connectionFactory.CreateConnection();
            // Selective-update like owners: identity (display name / profile / avatar) set on first
            // insert; on conflict only the stats + last_seen refresh. The composite key scopes the
            // conflict to this source, so a rival id that collides across sites is two rows, not one
            // overwriting the other (§1 I2; 0003's "band disjointness is not the only guard" rule).
            const string sql = """
                INSERT INTO performers
                    (source_id, performer_key, display_name, profile_url, avatar_url,
                     rating, completed_projects_count, completion_rate, live_presence, last_seen_at)
                VALUES
                    (@source, @key, @name, @profile, @avatar,
                     @rating, @completed, @completion, @presence, @seen)
                ON CONFLICT(source_id, performer_key) DO UPDATE SET
                    rating = excluded.rating,
                    completed_projects_count = excluded.completed_projects_count,
                    completion_rate = excluded.completion_rate,
                    live_presence = excluded.live_presence,
                    last_seen_at = excluded.last_seen_at;
                """;
            await connection.ExecuteAsync(new CommandDefinition(sql, new
            {
                source = performer.Source.Value,
                key = performer.PerformerKey,
                name = performer.DisplayName,
                profile = (string?)performer.ProfileUrl,
                avatar = (string?)performer.AvatarUrl,
                rating = (double?)performer.Rating,
                completed = (int?)performer.CompletedProjectsCount,
                completion = (double?)performer.CompletionRate,
                presence = (string?)performer.LivePresence,
                seen = performer.LastSeenAt.ToString("O"),
            }, cancellationToken: cancellationToken));
            return Result<bool>.Ok(true);
        }
        catch (SqliteException ex)
        {
            return Result<bool>.Err(DatabaseErrors.QueryFailed(nameof(UpsertAsync), ex));
        }
    }

    [ErrorOutcome(ErrorOutcome.Handled, Label = "Query failure surfaced as Result.Err")]
    public async Task<Result<Performer?>> GetByKeyAsync(SourceId source, string performerKey, CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = _connectionFactory.CreateConnection();
            const string sql = """
                SELECT source_id, performer_key, display_name, profile_url, avatar_url,
                       rating, completed_projects_count, completion_rate, live_presence, last_seen_at
                FROM performers
                WHERE source_id = @source AND performer_key = @key;
                """;
            var row = await connection.QueryFirstOrDefaultAsync<PerformerRow>(
                new CommandDefinition(sql, new { source = source.Value, key = performerKey }, cancellationToken: cancellationToken));
            return Result<Performer?>.Ok(row?.ToPerformer());
        }
        catch (SqliteException ex)
        {
            return Result<Performer?>.Err(DatabaseErrors.QueryFailed(nameof(GetByKeyAsync), ex));
        }
    }

    [ErrorOutcome(ErrorOutcome.Handled, Label = "Query failure surfaced as Result.Err")]
    public async Task<Result<IReadOnlyList<Performer>>> ListBySourceAsync(SourceId source, CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = _connectionFactory.CreateConnection();
            const string sql = """
                SELECT source_id, performer_key, display_name, profile_url, avatar_url,
                       rating, completed_projects_count, completion_rate, live_presence, last_seen_at
                FROM performers
                WHERE source_id = @source;
                """;
            var rows = await connection.QueryAsync<PerformerRow>(
                new CommandDefinition(sql, new { source = source.Value }, cancellationToken: cancellationToken));
            return Result<IReadOnlyList<Performer>>.Ok(rows.Select(r => r.ToPerformer()).ToList());
        }
        catch (SqliteException ex)
        {
            return Result<IReadOnlyList<Performer>>.Err(DatabaseErrors.QueryFailed(nameof(ListBySourceAsync), ex));
        }
    }

    private sealed class PerformerRow
    {
        public string source_id { get; set; } = string.Empty;
        public string performer_key { get; set; } = string.Empty;
        public string? display_name { get; set; }
        public string? profile_url { get; set; }
        public string? avatar_url { get; set; }
        public double? rating { get; set; }
        public int? completed_projects_count { get; set; }
        public double? completion_rate { get; set; }
        public string? live_presence { get; set; }
        public string? last_seen_at { get; set; }

        public Performer ToPerformer() => new()
        {
            Source = SourceId.From(source_id),
            PerformerKey = performer_key,
            DisplayName = display_name ?? string.Empty,
            ProfileUrl = profile_url,
            AvatarUrl = avatar_url,
            Rating = rating,
            CompletedProjectsCount = completed_projects_count,
            CompletionRate = completion_rate,
            LivePresence = live_presence,
            LastSeenAt = DateTimeOffset.TryParse(last_seen_at, null, System.Globalization.DateTimeStyles.RoundtripKind, out var t)
                ? t : DateTimeOffset.UtcNow,
        };
    }
}
