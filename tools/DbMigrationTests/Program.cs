using Microsoft.Data.Sqlite;
using MostaqlK.Core.Sources;
using MostaqlK.Infrastructure.Database;

namespace DbMigrationTests;

/// <summary>
/// Schema-level harness for the v1 -> v2 source-identity migration executed by
/// <c>SqliteConnectionFactory.EnsureSchema</c> (plan step 4 verification): builds a realistic
/// v1 database, opens it with the production factory, and asserts the upgrade, its
/// idempotency, fresh-install bootstrap, and composite-key collision behavior.
/// Run: dotnet run --project tools/DbMigrationTests
/// </summary>
internal static class Program
{
    private static int _failures;

    private static int Main(string[] args)
    {
        if (args.Length == 1)
        {
            // Real-data mode: the caller passes a COPY of a live v1 database (never the original).
            Console.WriteLine($"== upgrading real database copy: {args[0]} ==");
            var factory = new SqliteConnectionFactory(args[0]);
            using var connection = factory.CreateConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version;";
            Console.WriteLine($"user_version = {(long)command.ExecuteScalar()!}");
            Console.WriteLine("REAL-DATA UPGRADE OK");
            return 0;
        }

        var dir = Path.Combine(Path.GetTempPath(), "mostaqlk-dbtests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            TestV1ToV2Upgrade(Path.Combine(dir, "upgrade.db"));
            TestIdempotentRerun(Path.Combine(dir, "upgrade.db"));
            TestFreshInstallLandsAtV2(Path.Combine(dir, "fresh.db"));
            TestCrossSourceCollision(Path.Combine(dir, "upgrade.db"));
            TestNafezlyBandCoexistence(Path.Combine(dir, "coexist.db"));
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* temp scratch, best effort */ }
        }

        if (_failures == 0)
        {
            Console.WriteLine("ALL DB MIGRATION CHECKS PASSED");
            return 0;
        }

        Console.Error.WriteLine($"{_failures} CHECK(S) FAILED");
        return 1;
    }

    // ---------------------------------------------------------------- checks

    private static void TestV1ToV2Upgrade(string dbPath)
    {
        Console.WriteLine("== v1 -> v2 upgrade ==");
        File.Delete(dbPath);
        BuildV1Database(dbPath);

        var beforeIds = ReadIds(dbPath, "projects", "project_id");
        var beforeCounts = ReadCounts(dbPath);

        var factory = new SqliteConnectionFactory(dbPath);
        using (var connection = factory.CreateConnection())
        {
            var version = GetUserVersion(connection);
            Check(version == 3, $"user_version is 3 after full upgrade chain (got {version})");

            var columns = ReadColumnNames(connection, "projects");
            Check(columns.Contains("source_id") && columns.Contains("site_project_id"), "projects has source_id + site_project_id");
            Check(ReadColumnNames(connection, "owners").Contains("source_id"), "owners has source_id");
            Check(ReadColumnNames(connection, "discovery_backlog").Contains("source_id"), "discovery_backlog has source_id");

            var indexNames = ReadIndexNames(connection, "projects");
            Check(indexNames.Contains("ux_projects_source_site"), "unique index ux_projects_source_site exists");
            Check(ReadIndexNames(connection, "owners").Contains("ux_owners_source_owner"), "unique index ux_owners_source_owner exists");

            var backlogPk = ReadPrimaryKeyColumns(connection, "discovery_backlog");
            Check(backlogPk.SequenceEqual(["source_id", "project_id"]),
                $"discovery_backlog is keyed (source_id, project_id) (got {string.Join(",", backlogPk)})");

            Check(SingleScalar(connection, "SELECT COUNT(*) FROM projects WHERE source_id = 'mostaql' AND site_project_id = project_id;") == 3,
                "all 3 v1 rows backfilled to source mostaql with site_project_id = project_id");

            var afterIds = ReadIds(dbPath, "projects", "project_id");
            Check(beforeIds.SequenceEqual(afterIds), "project rowids (internal ids) unchanged - child/FTS references stay valid");

            var afterCounts = ReadCounts(dbPath);
            foreach (var (table, before) in beforeCounts)
            {
                Check(afterCounts[table] == before, $"{table} row count unchanged ({before} -> {afterCounts[table]})");
            }

            Check(SingleScalar(connection, "SELECT COUNT(*) FROM owners WHERE source_id = 'mostaql';") == 1, "owner row backfilled to mostaql");
            Check(SingleScalar(connection, "SELECT COUNT(*) FROM discovery_backlog WHERE source_id = 'mostaql';") == 1, "backlog row backfilled to mostaql");
        }
    }

    private static void TestIdempotentRerun(string dbPath)
    {
        Console.WriteLine("== re-run is a no-op ==");
        var beforeCounts = ReadCounts(dbPath);

        // A brand-new factory instance = fresh _schemaVerified=false, exactly what a second app
        // launch does against the already-migrated file.
        var factory = new SqliteConnectionFactory(dbPath);
        using (var connection = factory.CreateConnection())
        {
            Check(GetUserVersion(connection) == 3, "user_version stays 3");
        }

        var afterCounts = ReadCounts(dbPath);
        foreach (var (table, before) in beforeCounts)
        {
            Check(afterCounts[table] == before, $"{table} row count unchanged on re-run ({before} -> {afterCounts[table]})");
        }
    }

    private static void TestFreshInstallLandsAtV2(string dbPath)
    {
        Console.WriteLine("== fresh install bootstraps at v2 ==");
        File.Delete(dbPath);
        var factory = new SqliteConnectionFactory(dbPath);
        using var connection = factory.CreateConnection();

        Check(GetUserVersion(connection) == 3, "fresh db reaches the current user_version without running numbered steps");

        var columns = ReadColumnNames(connection, "projects");
        Check(columns.Contains("source_id") && columns.Contains("site_project_id"), "fresh projects table already carries the v2 columns");

        // A fresh-schema projects table declares source_id NOT NULL without a DEFAULT, so a
        // source-blind insert must fail (v1's bare insert can no longer sneak through).
        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO projects (project_id, title, url, discovered_at) VALUES (1, 't', 'u', 'now');";
            command.ExecuteNonQuery();
            Check(false, "insert without source_id rejected on fresh schema");
        }
        catch (SqliteException)
        {
            Check(true, "insert without source_id rejected on fresh schema");
        }
    }

    private static void TestCrossSourceCollision(string dbPath)
    {
        Console.WriteLine("== composite identity across sources ==");
        var mostaql = new StubDescriptor(SourceId.Mostaql, 0);
        var nafezly = new StubDescriptor(SourceId.From("nafezly"), 1_000_000);
        var registry = new SourceRegistry([mostaql, nafezly]);

        var keyA = new SourceProjectId(SourceId.Mostaql, 101);
        var keyB = new SourceProjectId(SourceId.From("nafezly"), 101);
        var internalA = registry.ToInternalId(keyA);
        var internalB = registry.ToInternalId(keyB);

        Check(internalA == 101, "mostaql band 0 keeps site id as internal id (v1 rows untouched)");
        Check(internalA != internalB, $"colliding site ids map to distinct internal ids ({internalA} vs {internalB})");
        Check(registry.FromInternalId(SourceId.Mostaql, internalA) == keyA, "round-trip mostaql");
        Check(registry.FromInternalId(nafezly.Id, internalB) == keyB, "round-trip nafezly");

        var factory = new SqliteConnectionFactory(dbPath);
        using var connection = factory.CreateConnection();

        // The migrated (mostaql, 101) row already exists at internal id 101; inserting nafezly's
        // colliding site id 101 must succeed (distinct internal id AND distinct unique-index pair).
        using (var insert = connection.CreateCommand())
        {
            insert.CommandText = """
                INSERT INTO projects (project_id, source_id, site_project_id, title, url, discovered_at)
                VALUES (@id, 'nafezly', 101, 'nafezly project', 'https://nafezly.com/projects/101', '2026-01-01T00:00:00Z');
                """;
            insert.Parameters.AddWithValue("@id", internalB);
            insert.ExecuteNonQuery();
        }

        Check(SingleScalar(connection, "SELECT COUNT(*) FROM projects WHERE site_project_id = 101;") == 2,
            "same site id stored once per source without collision");

        // The committed-knowledge set (what SqliteCommittedProvider feeds DiffEngine) must treat
        // the two as separate projects - the cross-source suppression bug class this refactor kills.
        var committed = new HashSet<SourceProjectId>();
        using (var query = connection.CreateCommand())
        {
            query.CommandText = "SELECT source_id, project_id FROM projects;";
            using var reader = query.ExecuteReader();
            while (reader.Read())
            {
                committed.Add(registry.FromInternalId(SourceId.From(reader.GetString(0)), reader.GetInt64(1)));
            }
        }

        Check(committed.Contains(keyA) && committed.Contains(keyB), "committed set holds both composites distinctly");

        // Re-inserting (mostaql, 101) under a different internal id must hit the composite unique
        // index - the (source_id, site_project_id) pair is the real identity.
        try
        {
            using var dup = connection.CreateCommand();
            dup.CommandText = "INSERT INTO projects (project_id, source_id, site_project_id, title, url, discovered_at) VALUES (999999, 'mostaql', 101, 'dup', 'u', 'now');";
            dup.ExecuteNonQuery();
            Check(false, "duplicate (mostaql, 101) rejected by ux_projects_source_site");
        }
        catch (SqliteException)
        {
            Check(true, "duplicate (mostaql, 101) rejected by ux_projects_source_site");
        }
    }

    // ---------------------------------------------------------------- fixtures

    /// <summary>
    /// Nafezly-band coexistence (contract §11, A3): drives the REAL nafezly internal-id band
    /// (1_000_000_000_000, <c>NafezlySource.InternalIdBand</c>) against a fresh production schema and
    /// inserts a mostaql row and a nafezly row that share the SAME site id (101) through raw SQL that
    /// mirrors the repository's columns. Asserts the composite identity guarantees each layer relies
    /// on: disjoint internal ids, the per-source composite UNIQUE indexes on projects/owners, the
    /// composite discovery_backlog key, and <see cref="SourceRegistry.FromInternalId"/> round-tripping
    /// both sources back to their (source, site id) composite. Unlike <see cref="TestCrossSourceCollision"/>
    /// (a toy 1_000_000 band on the migrated v1 db), this exercises the exact band nafezly ships with.
    /// </summary>
    private static void TestNafezlyBandCoexistence(string dbPath)
    {
        Console.WriteLine("== nafezly real-band coexistence ==");
        File.Delete(dbPath);
        const long nafezlyBand = 1_000_000_000_000;
        const long sharedSiteId = 101;

        var registry = new SourceRegistry([new StubDescriptor(SourceId.Mostaql, 0), new StubDescriptor(SourceId.From("nafezly"), nafezlyBand)]);
        var mostaqlKey = new SourceProjectId(SourceId.Mostaql, sharedSiteId);
        var nafezlyKey = new SourceProjectId(SourceId.From("nafezly"), sharedSiteId);
        var mostaqlInternal = registry.ToInternalId(mostaqlKey);
        var nafezlyInternal = registry.ToInternalId(nafezlyKey);

        Check(mostaqlInternal == sharedSiteId, "mostaql band 0 keeps site id as internal id");
        Check(nafezlyInternal == nafezlyBand + sharedSiteId, $"nafezly band maps site {sharedSiteId} to {nafezlyBand + sharedSiteId} (got {nafezlyInternal})");
        Check(mostaqlInternal != nafezlyInternal, "identical site ids never share an internal id across sources");

        var factory = new SqliteConnectionFactory(dbPath);
        using var connection = factory.CreateConnection();

        // Two projects, SAME site id 101, distinct source — inserted the way ProjectRepository would
        // (source_id + site_project_id + internal project_id all present).
        using (var insert = connection.CreateCommand())
        {
            insert.CommandText = """
                INSERT INTO projects (project_id, source_id, site_project_id, title, url, discovered_at, enrichment_status)
                VALUES
                    (@mostaqlId, 'mostaql', @site, 'mostaql 101', 'https://mostaql.com/u/projects/101', '2026-10-21T10:00:00Z', 'Enriched'),
                    (@nafezlyId, 'nafezly', @site, 'nafezly 101', 'https://nafezly.com/project/101', '2026-10-21T10:00:00Z', 'Enriched');
                """;
            insert.Parameters.AddWithValue("@mostaqlId", mostaqlInternal);
            insert.Parameters.AddWithValue("@nafezlyId", nafezlyInternal);
            insert.Parameters.AddWithValue("@site", sharedSiteId);
            insert.ExecuteNonQuery();
        }

        Check(SingleScalar(connection, "SELECT COUNT(*) FROM projects WHERE site_project_id = 101;") == 2,
            "composite (source_id, site_project_id) stores the same site id once per source");
        Check(SingleScalar(connection, "SELECT COUNT(*) FROM projects WHERE source_id = 'nafezly' AND project_id = 1000000000101;") == 1,
            "nafezly row keyed at the real-band internal id");

        // A second (nafezly, 101) must be rejected by ux_projects_source_site — the pair, not the
        // internal id, is the real identity.
        var dupProjectSql = """
            INSERT INTO projects (project_id, source_id, site_project_id, title, url, discovered_at, enrichment_status)
            VALUES (987654321, 'nafezly', 101, 'dup', 'u', 'now', 'Pending');
            """;
        Check(InsertRejected(connection, dupProjectSql), "duplicate (nafezly, 101) rejected by ux_projects_source_site");

        // Owners are keyed by a band-differentiated INTERNAL owner id (mostaql 900, nafezly band+900),
        // so two sources' owners never collide on the owner_id PK; ux_owners_source_owner is the
        // enforceable composite identity that also rejects a re-insert of the same (source, owner) pair.
        using (var owners = connection.CreateCommand())
        {
            owners.CommandText = """
                INSERT INTO owners (owner_id, source_id, name, last_seen_at)
                VALUES (900, 'mostaql', 'م', '2026-10-21T10:00:00Z'), (@nafezlyOwnerId, 'nafezly', 'ن', '2026-10-21T10:00:00Z');
                """;
            owners.Parameters.AddWithValue("@nafezlyOwnerId", nafezlyBand + 900);
            owners.ExecuteNonQuery();
        }

        Check(SingleScalar(connection, "SELECT COUNT(*) FROM owners;") == 2,
            "band-differentiated owner ids coexist across sources");
        var dupOwnerSql = """
            INSERT INTO owners (owner_id, source_id, name, last_seen_at) VALUES (1000000000900, 'nafezly', 'dup', '2026-10-21T10:00:00Z');
            """;
        Check(InsertRejected(connection, dupOwnerSql), "re-insert of (nafezly, band owner id) rejected (owner_id PK + ux_owners_source_owner)");

        // Discovery backlog is keyed (source_id, project_id) — internal ids differ, both survive; a
        // re-enqueue of the same composite is rejected by the composite primary key.
        using (var backlog = connection.CreateCommand())
        {
            backlog.CommandText = """
                INSERT INTO discovery_backlog (source_id, project_id, retry_count)
                VALUES ('mostaql', @mid, 0), ('nafezly', @nid, 0);
                """;
            backlog.Parameters.AddWithValue("@mid", mostaqlInternal);
            backlog.Parameters.AddWithValue("@nid", nafezlyInternal);
            backlog.ExecuteNonQuery();
        }

        Check(SingleScalar(connection, "SELECT COUNT(*) FROM discovery_backlog;") == 2, "backlog holds both sources' rows under its composite key");
        var dupBacklogSql = """
            INSERT INTO discovery_backlog (source_id, project_id) VALUES ('nafezly', 1000000000101);
            """;
        Check(InsertRejected(connection, dupBacklogSql), "re-enqueue of (nafezly, band-internal id) rejected by composite PK");

        // FromInternalId round-trips every stored row back to its (source, site id) composite.
        var roundTripped = new HashSet<SourceProjectId>();
        using (var query = connection.CreateCommand())
        {
            query.CommandText = "SELECT source_id, project_id FROM projects;";
            using var reader = query.ExecuteReader();
            while (reader.Read())
            {
                roundTripped.Add(registry.FromInternalId(SourceId.From(reader.GetString(0)), reader.GetInt64(1)));
            }
        }

        Check(roundTripped.Contains(mostaqlKey) && roundTripped.Contains(nafezlyKey),
            "FromInternalId resolves both colliding site ids back to their distinct composites");
    }

    /// <summary>True when the statement throws a SqliteException (i.e. a constraint rejected it).</summary>
    private static bool InsertRejected(SqliteConnection connection, string sql)
    {
        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
            return false;
        }
        catch (SqliteException)
        {
            return true;
        }
    }


    /// <summary>The historical v1 bootstrap schema, byte-for-byte what shipped pre-refactor.</summary>
    private const string V1SchemaSql = """
        CREATE TABLE IF NOT EXISTS projects (
            project_id INTEGER PRIMARY KEY,
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

        CREATE TABLE IF NOT EXISTS owners (
            owner_id INTEGER PRIMARY KEY,
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

    private static void BuildV1Database(string dbPath)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString());
        connection.Open();

        Exec(connection, V1SchemaSql);
        Exec(connection, """
            INSERT INTO projects (project_id, title, url, description, budget, owner_id, discovered_at, enrichment_status)
            VALUES
                (101, 'مشروع أول', 'https://mostaql.com/u/projects/101', 'وصف 1', '500 ر.س', 5001, '2026-10-19T10:00:00Z', 'Enriched'),
                (102, 'مشروع ثانٍ', 'https://mostaql.com/u/projects/102', 'وصف 2', NULL, 5001, '2026-10-20T10:00:00Z', 'Enriched'),
                (103, 'مشروع ثالث', 'https://mostaql.com/u/projects/103', NULL, NULL, NULL, '2026-10-21T10:00:00Z', 'Pending');

            INSERT INTO owners (owner_id, name, last_seen_at) VALUES (5001, 'عميل', '2026-10-21T10:00:00Z');
            INSERT INTO project_skills (project_id, name) VALUES (101, 'تصميم'), (101, 'شعار'), (102, 'برمجة');
            INSERT INTO assets (project_id, file_name) VALUES (101, 'a.pdf');
            INSERT INTO projects_fts (project_id, title, description, skills)
            VALUES (101, 'مشروع أول', 'وصف 1', 'تصميم شعار'), (102, 'مشروع ثانٍ', 'وصف 2', 'برمجة'), (103, 'مشروع ثالث', '', '');
            INSERT INTO discovery_backlog (project_id) VALUES (103);
            """);
        Exec(connection, "PRAGMA user_version = 1;");
    }

    private sealed class StubDescriptor(SourceId id, long band) : ISourceDescriptor
    {
        public SourceId Id { get; } = id;
        public string DisplayNameAr => Id.Value;
        public string DisplayNameLatin => Id.Value;
        public string Domain => Id.Value + ".test";
        public string RailGlyph => "X";
        public BrandTokenSet Brands => new("#000000", "#000000", "#000000", "#FFFFFF", "#0000FF", "#4040FF", "#555555", "#555555", "#24555555", "#111111");
        public int DefaultPollIntervalSeconds => 30;
        public int DefaultRequestsPerMinute => 2;
        public string CurrencySuffix => "";
        public string? OwnerFallbackMeta => null;
        public SourceCapabilities Capabilities => new(true, true, false);
        // A1 added Profile/Selectors/SchemaMap to ISourceDescriptor; the migration harness never
        // parses or renders, so it answers with the shared empty data singletons.
        public MostaqlK.Core.Parsing.ParseProfile Profile => MostaqlK.Core.Parsing.ParseProfile.Empty;
        public MostaqlK.Core.Parsing.SelectorMap Selectors => MostaqlK.Core.Parsing.SelectorMap.Empty;
        public IReadOnlyList<MostaqlK.Core.Sources.FieldSpec> SchemaMap => [];
        public string CookieSecretKey => "cookie_" + Id.Value;
        public string? SessionCookieName => null;
        public string LegacyCookieSecretKey => "cookie_legacy_" + Id.Value;
        public string LegacyCookieEnvInlineVar => "TEST_COOKIE_" + Id.Value.ToUpperInvariant();
        public string LegacyCookieEnvFileVar => "TEST_COOKIE_" + Id.Value.ToUpperInvariant() + "_FILE";
        public string SettingsContainerId => Id.Value;
        public string UserAgent => "test-agent";
        public string AcceptHeader => "text/html";
        public string AcceptLanguageHeader => "en";
        public long InternalIdBand => band;
        public string BuildListingUrl(string? queryParams) => $"https://{Domain}/projects";
        public string BuildProjectUrl(long siteProjectId) => $"https://{Domain}/projects/{siteProjectId}";
        public string BuildAbsoluteUrl(string pathOrUrl) => pathOrUrl;
        public bool IsSameSiteUrl(string url) => url.Contains(Domain, StringComparison.OrdinalIgnoreCase);
    }

    // ---------------------------------------------------------------- helpers

    private static void Check(bool condition, string label)
    {
        if (condition)
        {
            Console.WriteLine($"  PASS  {label}");
        }
        else
        {
            _failures++;
            Console.Error.WriteLine($"  FAIL  {label}");
        }
    }

    private static void Exec(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static long GetUserVersion(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        return (long)command.ExecuteScalar()!;
    }

    private static long SingleScalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (long)command.ExecuteScalar()!;
    }

    private static List<long> ReadIds(string dbPath, string table, string column)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {column} FROM {table} ORDER BY {column};";
        var ids = new List<long>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            ids.Add(reader.GetInt64(0));
        }

        return ids;
    }

    private static Dictionary<string, long> ReadCounts(string dbPath)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString());
        connection.Open();
        var counts = new Dictionary<string, long>();
        foreach (var table in new[] { "projects", "owners", "project_skills", "assets", "discovery_backlog", "projects_fts" })
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT COUNT(*) FROM {table};";
            counts[table] = (long)command.ExecuteScalar()!;
        }

        return counts;
    }

    private static HashSet<string> ReadColumnNames(SqliteConnection connection, string table)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({table});";
        var names = new HashSet<string>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            names.Add(reader.GetString(1));
        }

        return names;
    }

    private static HashSet<string> ReadIndexNames(SqliteConnection connection, string table)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA index_list({table});";
        var names = new HashSet<string>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            names.Add(reader.GetString(1));
        }

        return names;
    }

    /// <summary>The table's PRIMARY KEY columns in key order (PRAGMA table_info pk&gt;0 gives position).</summary>
    private static List<string> ReadPrimaryKeyColumns(SqliteConnection connection, string table)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({table});";
        var keyed = new List<(int Position, string Name)>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var position = reader.GetInt32(5);
            if (position > 0)
            {
                keyed.Add((position, reader.GetString(1)));
            }
        }

        return keyed.OrderBy(k => k.Position).Select(k => k.Name).ToList();
    }
}
