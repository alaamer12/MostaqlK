using Dapper;
using Microsoft.Data.Sqlite;
using MostaqlK.Core;
using MostaqlK.Core.Sources;
using MostaqlK.Models;

namespace MostaqlK.Infrastructure.Database;

/// <inheritdoc cref="IProfileSnapshotRepository"/>
public sealed class ProfileSnapshotRepository : IProfileSnapshotRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;

    public ProfileSnapshotRepository(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    [ErrorOutcome(ErrorOutcome.Handled, Label = "Insert failure surfaced as Result<long>.Err")]
    public async Task<Result<long>> InsertAsync(ProfileSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = _connectionFactory.CreateConnection();
            // R2.8 invariants enforced here so no caller can violate them: a failed attempt
            // stores NO fields (so it cannot be mistaken for profile data), and a success
            // stores NO error_kind. fields_json is public scraped facts only — never a secret.
            var fieldsJson = snapshot.Ok ? snapshot.FieldsJson : null;
            var errorKind = snapshot.Ok ? null : snapshot.ErrorKind;

            const string sql = """
                INSERT INTO profile_snapshots
                    (source_id, crawled_at, ok, fields_json, error_kind, http_status, body_bytes, pages_read)
                VALUES
                    (@source, @crawled_at, @ok, @fields_json, @error_kind, @http_status, @body_bytes, @pages_read);
                SELECT last_insert_rowid();
                """;

            var id = await connection.QuerySingleAsync<long>(new CommandDefinition(sql, new
            {
                source = snapshot.Source.Value,
                crawled_at = snapshot.CrawledAt.ToString("O"),
                ok = snapshot.Ok ? 1 : 0,
                fields_json = (string?)fieldsJson,
                error_kind = (string?)errorKind,
                http_status = snapshot.HttpStatus,
                body_bytes = snapshot.BodyBytes,
                pages_read = snapshot.PagesRead,
            }, cancellationToken: cancellationToken));

            return Result<long>.Ok(id);
        }
        catch (SqliteException ex)
        {
            return Result<long>.Err(DatabaseErrors.QueryFailed(nameof(InsertAsync), ex));
        }
    }

    [ErrorOutcome(ErrorOutcome.Handled, Label = "Query failure surfaced as Result.Err")]
    public async Task<Result<ProfileSnapshot?>> GetLatestOkAsync(SourceId source, CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = _connectionFactory.CreateConnection();
            // The read path IS the safety guarantee (R2.8): only ok = 1 rows are ever read back,
            // and only for this source. id DESC breaks crawled_at ties so the newest insert wins.
            const string sql = """
                SELECT id, source_id, crawled_at, ok, fields_json, error_kind, http_status, body_bytes, pages_read
                FROM profile_snapshots
                WHERE source_id = @source AND ok = 1
                ORDER BY crawled_at DESC, id DESC
                LIMIT 1;
                """;

            var row = await connection.QueryFirstOrDefaultAsync<SnapshotRow>(
                new CommandDefinition(sql, new { source = source.Value }, cancellationToken: cancellationToken));

            return Result<ProfileSnapshot?>.Ok(row?.ToSnapshot());
        }
        catch (SqliteException ex)
        {
            return Result<ProfileSnapshot?>.Err(DatabaseErrors.QueryFailed(nameof(GetLatestOkAsync), ex));
        }
    }

    [ErrorOutcome(ErrorOutcome.Handled, Label = "Query failure surfaced as Result<int>.Err")]
    public async Task<Result<int>> PruneToNewestAsync(SourceId source, int keep, CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = _connectionFactory.CreateConnection();
            // Retention is per-source by construction: the NOT-IN subquery is filtered on the same
            // source_id, so pruning Mostaql can never delete a Nafezly row (rule I3 / isolation).
            // Follows the existing CleanOldBacklogAsync precedent (delete by cutoff from the
            // WorkerPool cleanup block); the newest-20 cap keeps the diagnosable history.
            const string sql = """
                DELETE FROM profile_snapshots
                WHERE source_id = @source
                  AND id NOT IN (
                      SELECT id FROM profile_snapshots
                      WHERE source_id = @source
                      ORDER BY crawled_at DESC, id DESC
                      LIMIT @keep
                  );
                """;

            var removed = await connection.ExecuteAsync(
                new CommandDefinition(sql, new { source = source.Value, keep }, cancellationToken: cancellationToken));
            return Result<int>.Ok(removed);
        }
        catch (SqliteException ex)
        {
            return Result<int>.Err(DatabaseErrors.QueryFailed(nameof(PruneToNewestAsync), ex));
        }
    }

    private sealed class SnapshotRow
    {
        public long id { get; set; }
        public string source_id { get; set; } = string.Empty;
        public string crawled_at { get; set; } = string.Empty;
        public int ok { get; set; }
        public string? fields_json { get; set; }
        public string? error_kind { get; set; }
        public int? http_status { get; set; }
        public long? body_bytes { get; set; }
        public int? pages_read { get; set; }

        public ProfileSnapshot ToSnapshot() => new(
            id,
            SourceId.From(source_id),
            ok == 1,
            fields_json,
            error_kind,
            http_status,
            body_bytes,
            pages_read,
            DateTimeOffset.TryParse(crawled_at, null, System.Globalization.DateTimeStyles.RoundtripKind, out var t)
                ? t
                : DateTimeOffset.UtcNow);
    }
}
