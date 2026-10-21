using Microsoft.Data.Sqlite;
using MostaqlK.Core.Platform;

namespace MostaqlK.Infrastructure.Database;

/// <summary>
/// Creates configured, already-open <see cref="SqliteConnection"/> instances pointing at the
/// app's local database file under app data, ensuring the schema/migrations have been applied
/// (stepwise <c>PRAGMA user_version</c> runner: 0→1 bootstrap, 1→2 source identity).
/// </summary>
public sealed class SqliteConnectionFactory
{
    /// <summary>
    /// Current schema version expected by this build. Numbered migrations live as
    /// <c>Migrations/NNNN_*.sql</c> reference files; the executable copy of each step is the
    /// matching SQL constant below, applied in order by <see cref="EnsureSchema"/>.
    /// </summary>
    private const int CurrentSchemaVersion = 2;

    private readonly string _connectionString;
    private readonly object _migrationLock = new();
    private bool _schemaVerified;

    public SqliteConnectionFactory()
        : this(AppPaths.DatabasePath)
    {
    }

    public SqliteConnectionFactory(string dbPath)
    {
        _connectionString = new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString();
    }

    /// <summary>
    /// Opens a new connection to the local database, verifying (and, when behind, migrating)
    /// the schema first.
    /// </summary>
    public SqliteConnection CreateConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();

        // WAL mode lets readers (e.g. the search/feed queries) proceed concurrently with
        // writers (PollService/EnrichmentWorker/AssetDownloadService), instead of the default
        // rollback-journal mode where a writer's exclusive lock blocks readers entirely. The
        // busy_timeout is a second line of defense: if a writer is still mid-transaction when a
        // read starts, SQLite will retry internally for up to 5s instead of failing immediately
        // with SQLITE_BUSY (which previously surfaced as an intermittent, hard-to-reproduce
        // "search returns 0 results with no visible error" bug caused by a lock momentarily
        // held by a concurrent background write).
        using (var pragmaCommand = connection.CreateCommand())
        {
            pragmaCommand.CommandText = "PRAGMA journal_mode = WAL; PRAGMA busy_timeout = 5000;";
            pragmaCommand.ExecuteNonQuery();
        }

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

            if (currentVersion > CurrentSchemaVersion)
            {
                throw DatabaseErrors.SchemaVersionMismatch(currentVersion, CurrentSchemaVersion);
            }

            // Stepwise runner: each step upgrades exactly one version and is applied only when
            // the database is at that version, so re-runs are no-ops and a downgrade by an older
            // build surfaces as the mismatch throw above rather than a half-applied schema.
            // The bootstrap schema already lands at v2, so version 0 never runs the 0002 step.
            if (currentVersion == 0)
            {
                RunStep(connection, InitialSchemaSql);
            }
            else if (currentVersion == 1)
            {
                RunStep(connection, Migration0002Sql);
            }

            SetUserVersion(connection, CurrentSchemaVersion);

            // One-time-per-process backfill: covers rows written by older builds, where
            // `InsertSummaryAsync` did not yet write to `projects_fts`, so any row inserted
            // while still "Pending" was never searchable until enriched. Idempotent - only
            // touches rows that don't have an FTS row yet.
            BackfillMissingFtsRows(connection);

            // Idempotent add-on table (see `SecretsTableSql`). Deliberately *not* a user_version
            // bump: existing databases stay schema-compatible, and adding a step without a real
            // migration path would make already-installed apps throw `SchemaVersionMismatch`.
            EnsureSecretsTable(connection);

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

    /// <summary>
    /// Runs one migration step inside a transaction, so a failure mid-step leaves
    /// <c>user_version</c> untouched and the next launch retries the same step cleanly.
    /// </summary>
    private static void RunStep(SqliteConnection connection, string stepSql)
    {
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = stepSql;
        command.ExecuteNonQuery();
        transaction.Commit();
    }

    private static void BackfillMissingFtsRows(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO projects_fts (project_id, title, description, skills)
            SELECT p.project_id, p.title, COALESCE(p.description, ''),
                   COALESCE((SELECT group_concat(name, ' ') FROM project_skills s WHERE s.project_id = p.project_id), '')
            FROM projects p
            WHERE p.project_id NOT IN (SELECT project_id FROM projects_fts);
            """;
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Creates the encrypted secrets table if it is missing. Values are ciphertext produced by
    /// <c>SecretProtector</c>, so the column stays opaque even to someone opening the .db file.
    /// </summary>
    private static void EnsureSecretsTable(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = SecretsTableSql;
        command.ExecuteNonQuery();
    }

    private const string SecretsTableSql = """
        CREATE TABLE IF NOT EXISTS app_secrets (
            key TEXT PRIMARY KEY,
            value TEXT NOT NULL,
            updated_at TEXT NOT NULL
        );
        """;

    /// <summary>
    /// v1 -&gt; v2 step (see <c>Migrations/0002_add_source_identity.sql</c> for the same
    /// statements as a readable reference file): <c>project_id</c>/<c>owner_id</c> become
    /// INTERNAL row ids (descriptor band + site id); Mostaql's band is 0, so existing values and
    /// every child-table / FTS reference to them stay valid untouched. The site's own numeric id
    /// moves to <c>site_project_id</c>, unique per source. The <c>DEFAULT 'mostaql'</c> on the
    /// added columns only backfills pre-existing v1 rows; new writes always set source explicitly.
    /// </summary>
    private const string Migration0002Sql = """
        ALTER TABLE projects ADD COLUMN source_id TEXT NOT NULL DEFAULT 'mostaql';
        ALTER TABLE projects ADD COLUMN site_project_id INTEGER;
        UPDATE projects SET site_project_id = project_id WHERE site_project_id IS NULL;
        CREATE UNIQUE INDEX IF NOT EXISTS ux_projects_source_site ON projects(source_id, site_project_id);
        ALTER TABLE owners ADD COLUMN source_id TEXT NOT NULL DEFAULT 'mostaql';
        ALTER TABLE discovery_backlog ADD COLUMN source_id TEXT NOT NULL DEFAULT 'mostaql';
        """;

    /// <summary>
    /// Bootstrap ("version 0") schema - lands directly at the current v2 shape so fresh installs
    /// never run the 0002 step; see <c>Migrations/0001_initial_schema.sql</c> for the historical
    /// v1 statements.
    /// </summary>
    private const string InitialSchemaSql = """
        CREATE TABLE IF NOT EXISTS projects (
            project_id INTEGER PRIMARY KEY,
            source_id TEXT NOT NULL,
            site_project_id INTEGER NOT NULL,
            title TEXT NOT NULL,
            url TEXT NOT NULL,
            client_name TEXT,
            publish_time_number INTEGER,
            publish_time_text TEXT,
            proposal_count INTEGER,
            proposal_count_text TEXT,
            description TEXT,
            budget TEXT,
            delivery_days INTEGER,
            project_status TEXT,
            owner_id INTEGER,
            is_unread INTEGER NOT NULL DEFAULT 1,
            enrichment_status TEXT NOT NULL DEFAULT 'Pending',
            discovered_at TEXT NOT NULL,
            enriched_at TEXT
        );

        CREATE UNIQUE INDEX IF NOT EXISTS ux_projects_source_site ON projects(source_id, site_project_id);

        CREATE TABLE IF NOT EXISTS owners (
            owner_id INTEGER PRIMARY KEY,
            source_id TEXT NOT NULL,
            name TEXT NOT NULL,
            profile_url TEXT,
            avatar_url TEXT,
            rating REAL,
            completed_projects_count INTEGER,
            hiring_rate_percent REAL,
            registered_at TEXT,
            open_projects_count INTEGER,
            in_progress_projects_count INTEGER,
            ongoing_communications_count INTEGER,
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

        CREATE TABLE IF NOT EXISTS discovery_backlog (
            project_id INTEGER PRIMARY KEY,
            source_id TEXT NOT NULL,
            discovered_at DATETIME DEFAULT CURRENT_TIMESTAMP,
            retry_count INTEGER DEFAULT 0
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
