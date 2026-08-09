using Microsoft.Data.Sqlite;

namespace MostaqlK.Infrastructure.Database;

/// <summary>
/// Creates configured, already-open <see cref="SqliteConnection"/> instances pointing at the
/// app's local database file under app data, ensuring the schema/migrations have been applied
/// (bootstrap "create if not exists" migration, tracked via <c>PRAGMA user_version</c>).
/// </summary>
public sealed class SqliteConnectionFactory
{
    /// <summary>
    /// Current schema version expected by this build. V1 only ever has a single bootstrap
    /// migration - a real numbered migration set (see <c>Migrations/README.md</c>) is deferred
    /// until a V2 schema change actually requires one.
    /// </summary>
    private const int CurrentSchemaVersion = 1;

    private readonly string _connectionString;
    private readonly object _migrationLock = new();
    private bool _schemaVerified;

    public SqliteConnectionFactory()
    {
        var dbPath = Path.Combine(FileSystem.AppDataDirectory, "mostaqlk.db");
        _connectionString = new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString();
    }

    /// <summary>
    /// Opens a new connection to the local database, verifying (and, on a fresh database,
    /// bootstrapping) the schema first.
    /// </summary>
    public SqliteConnection CreateConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();

        EnsureSchema(connection);

        return connection;
    }

    private void EnsureSchema(SqliteConnection connection)
    {
        lock (_migrationLock)
        {
            if (_schemaVerified)
            {
                return;
            }

            var currentVersion = GetUserVersion(connection);

            if (currentVersion == 0)
            {
                RunInitialMigration(connection);
                SetUserVersion(connection, CurrentSchemaVersion);
            }
            else if (currentVersion != CurrentSchemaVersion)
            {
                throw new DatabaseSchemaException(
                    $"Database schema version {currentVersion} does not match the version expected " +
                    $"by this build ({CurrentSchemaVersion}) and no migration path exists yet.");
            }

            _schemaVerified = true;
        }
    }

    private static long GetUserVersion(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        return (long)command.ExecuteScalar()!;
    }

    private static void SetUserVersion(SqliteConnection connection, int version)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA user_version = {version};";
        command.ExecuteNonQuery();
    }

    private static void RunInitialMigration(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = InitialSchemaSql;
        command.ExecuteNonQuery();
        transaction.Commit();
    }

    /// <summary>
    /// Bootstrap ("version 1") schema - see <c>Migrations/0001_initial_schema.sql</c> for the
    /// same statements kept as a readable reference file.
    /// </summary>
    private const string InitialSchemaSql = """
        CREATE TABLE IF NOT EXISTS projects (
            project_id INTEGER PRIMARY KEY,
            title TEXT NOT NULL,
            url TEXT NOT NULL,
            client_name TEXT,
            posted_relative TEXT,
            proposal_count INTEGER,
            description TEXT,
            budget TEXT,
            delivery_days INTEGER,
            owner_id INTEGER,
            is_unread INTEGER NOT NULL DEFAULT 1,
            enrichment_status TEXT NOT NULL DEFAULT 'Pending',
            discovered_at TEXT NOT NULL,
            enriched_at TEXT
        );

        CREATE TABLE IF NOT EXISTS owners (
            owner_id INTEGER PRIMARY KEY,
            name TEXT NOT NULL,
            profile_url TEXT,
            avatar_url TEXT,
            rating REAL,
            completed_projects_count INTEGER,
            hiring_rate_percent INTEGER,
            last_seen_at TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS project_skills (
            project_id INTEGER NOT NULL,
            name TEXT NOT NULL,
            url TEXT,
            FOREIGN KEY (project_id) REFERENCES projects (project_id)
        );

        CREATE TABLE IF NOT EXISTS assets (
            asset_id INTEGER PRIMARY KEY AUTOINCREMENT,
            project_id INTEGER NOT NULL,
            file_name TEXT NOT NULL,
            url TEXT,
            raw_url TEXT,
            local_path TEXT,
            size_bytes INTEGER,
            extension TEXT,
            requires_auth INTEGER NOT NULL DEFAULT 0,
            size_text TEXT,
            FOREIGN KEY (project_id) REFERENCES projects (project_id)
        );

        CREATE VIRTUAL TABLE IF NOT EXISTS projects_fts USING fts5(
            project_id UNINDEXED,
            title,
            description,
            skills,
            tokenize = 'unicode61 remove_diacritics 2'
        );
        """;
}
