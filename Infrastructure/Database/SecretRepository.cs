using Microsoft.Data.Sqlite;
using MostaqlK.Core;
using MostaqlK.Infrastructure.Security;

namespace MostaqlK.Infrastructure.Database;

/// <inheritdoc cref="ISecretRepository"/>
public sealed class SecretRepository : ISecretRepository
{
    /// <summary>Key under which the Mostaql session cookie header is stored.</summary>
    public const string MostaqlCookieKey = "mostaql_cookie";

    private readonly SqliteConnectionFactory _connectionFactory;

    public SecretRepository(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    [ErrorOutcome(ErrorOutcome.Handled, Label = "Query failure surfaced as Result.Err")]
    public async Task<Result<string?>> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = _connectionFactory.CreateConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT value FROM app_secrets WHERE key = @key;";
            command.Parameters.AddWithValue("@key", key);

            var stored = (string?)await command.ExecuteScalarAsync(cancellationToken);
            // A blob written by another Windows user (or a corrupted one) decrypts to null; that
            // is "no cookie configured", not an error the pipeline should react to.
            return Result<string?>.Ok(SecretProtector.TryUnprotect(stored));
        }
        catch (SqliteException ex)
        {
            return Result<string?>.Err(DatabaseErrors.QueryFailed(nameof(GetAsync), ex));
        }
    }

    [ErrorOutcome(ErrorOutcome.Handled, Label = "Query failure surfaced as Result<bool>.Err")]
    public async Task<Result<bool>> SetAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = _connectionFactory.CreateConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO app_secrets (key, value, updated_at)
                VALUES (@key, @value, @updated_at)
                ON CONFLICT (key) DO UPDATE SET value = excluded.value, updated_at = excluded.updated_at;
                """;
            command.Parameters.AddWithValue("@key", key);
            command.Parameters.AddWithValue("@value", SecretProtector.Protect(value));
            command.Parameters.AddWithValue("@updated_at", DateTime.UtcNow.ToString("O"));

            await command.ExecuteNonQueryAsync(cancellationToken);
            return Result<bool>.Ok(true);
        }
        catch (SqliteException ex)
        {
            return Result<bool>.Err(DatabaseErrors.QueryFailed(nameof(SetAsync), ex));
        }
    }

    [ErrorOutcome(ErrorOutcome.Handled, Label = "Query failure surfaced as Result<bool>.Err")]
    public async Task<Result<bool>> DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = _connectionFactory.CreateConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM app_secrets WHERE key = @key;";
            command.Parameters.AddWithValue("@key", key);

            await command.ExecuteNonQueryAsync(cancellationToken);
            return Result<bool>.Ok(true);
        }
        catch (SqliteException ex)
        {
            return Result<bool>.Err(DatabaseErrors.QueryFailed(nameof(DeleteAsync), ex));
        }
    }

    [ErrorOutcome(ErrorOutcome.Handled, Label = "Query failure surfaced as Result.Err")]
    public async Task<Result<DateTime?>> GetUpdatedAtAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = _connectionFactory.CreateConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT updated_at FROM app_secrets WHERE key = @key;";
            command.Parameters.AddWithValue("@key", key);

            var stored = (string?)await command.ExecuteScalarAsync(cancellationToken);
            return Result<DateTime?>.Ok(
                DateTime.TryParse(stored, null, System.Globalization.DateTimeStyles.RoundtripKind, out var parsed)
                    ? parsed
                    : null);
        }
        catch (SqliteException ex)
        {
            return Result<DateTime?>.Err(DatabaseErrors.QueryFailed(nameof(GetUpdatedAtAsync), ex));
        }
    }
}
