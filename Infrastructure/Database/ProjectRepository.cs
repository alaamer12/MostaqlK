using Dapper;
using Microsoft.Data.Sqlite;
using MostaqlK.Core;
using MostaqlK.Core.Sources;
using MostaqlK.Models;
using MostaqlK.Services.Diagnostics;

namespace MostaqlK.Infrastructure.Database;

/// <inheritdoc cref="IProjectRepository"/>
public sealed class ProjectRepository : IProjectRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly SourceRegistry _registry;

    public ProjectRepository(SqliteConnectionFactory connectionFactory, SourceRegistry registry)
    {
        _connectionFactory = connectionFactory;
        _registry = registry;
    }

    private long InternalId(SourceProjectId key) => _registry.ToInternalId(key);

    private SourceProjectId KeyFromRow(string sourceId, long internalId) =>
        _registry.FromInternalId(SourceId.From(sourceId), internalId);

    [ErrorOutcome(ErrorOutcome.Handled, Label = "Query failure surfaced as Result<bool>.Err")]
    public async Task<Result<bool>> InsertSummaryAsync(ProjectSummary project, CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = _connectionFactory.CreateConnection();
            using var transaction = connection.BeginTransaction();

            var internalId = InternalId(new SourceProjectId(project.Source, project.ProjectId));

            int rowsAffected;
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                // Write-once: a project row is never overwritten once it exists (no-update policy).
                command.CommandText = """
                    INSERT OR IGNORE INTO projects
                        (project_id, source_id, site_project_id, title, url, client_name,
                         publish_time_number, publish_time_text,
                         proposal_count, proposal_count_text, description,
                         is_unread, enrichment_status, discovered_at)
                    VALUES
                        (@project_id, @source_id, @site_project_id, @title, @url, @client_name,
                         @publish_time_number, @publish_time_text,
                         @proposal_count, @proposal_count_text, @description,
                         @is_unread, @enrichment_status, @discovered_at);
                    """;
                command.Parameters.AddWithValue("@project_id", internalId);
                command.Parameters.AddWithValue("@source_id", project.Source.Value);
                command.Parameters.AddWithValue("@site_project_id", project.ProjectId);
                command.Parameters.AddWithValue("@title", project.Title);
                command.Parameters.AddWithValue("@url", project.Url);
                command.Parameters.AddWithValue("@client_name", project.ClientName);
                command.Parameters.AddWithValue("@publish_time_number", project.PublishTimeNumber);
                command.Parameters.AddWithValue("@publish_time_text", project.PublishTimeText);
                command.Parameters.AddWithValue("@proposal_count", project.ProposalCount);
                command.Parameters.AddWithValue("@proposal_count_text", project.ProposalCountText);
                command.Parameters.AddWithValue("@description", project.Description);
                command.Parameters.AddWithValue("@is_unread", project.IsUnread ? 1 : 0);
                command.Parameters.AddWithValue("@enrichment_status", project.EnrichmentStatus.ToString());
                command.Parameters.AddWithValue("@discovered_at", project.DiscoveredAt.ToString("O"));

                rowsAffected = await command.ExecuteNonQueryAsync(cancellationToken);
            }

            if (rowsAffected > 0)
            {
                // Keep `projects_fts` live: without this, a newly discovered "Pending" project
                // is invisible to search until it gets enriched (`UpsertDetailsAsync` re-syncs
                // the fts row) or the process restarts (one-time backfill in
                // `SqliteConnectionFactory`). Description/skills are filled in on enrichment.
                using var insertFtsCommand = connection.CreateCommand();
                insertFtsCommand.Transaction = transaction;
                insertFtsCommand.CommandText = """
                    INSERT INTO projects_fts (project_id, title, description, skills)
                    VALUES (@project_id, @title, @description, '');
                    """;
                insertFtsCommand.Parameters.AddWithValue("@project_id", internalId);
                insertFtsCommand.Parameters.AddWithValue("@title", project.Title);
                insertFtsCommand.Parameters.AddWithValue("@description", project.Description);
                await insertFtsCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            transaction.Commit();
            return Result<bool>.Ok(rowsAffected > 0);
        }
        catch (SqliteException ex)
        {
            return Result<bool>.Err(DatabaseErrors.QueryFailed(nameof(InsertSummaryAsync), ex));
        }
    }

    [ErrorOutcome(ErrorOutcome.Handled, Label = "Query failure surfaced as Result<bool>.Err")]
    public async Task<Result<bool>> UpsertDetailsAsync(ProjectDetails details, CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = _connectionFactory.CreateConnection();
            using var transaction = connection.BeginTransaction();

            var internalId = InternalId(new SourceProjectId(details.Source, details.ProjectId));
            // Owner ids are per-source too (some are username hashes); store the internal form.
            var internalOwnerId = details.Owner.OwnerId == 0
                ? (long?)null
                : InternalId(new SourceProjectId(details.Owner.Source, details.Owner.OwnerId));

            // The one exception to the write-once policy: completing a previously
            // "Pending" row with the enriched fields fetched from the detail page.
            // Fields not yet known at discovery time (description/budget/etc.) are
            // filled in here; the project_id itself is never duplicated.
            using (var upsertCommand = connection.CreateCommand())
            {
                upsertCommand.Transaction = transaction;
                upsertCommand.CommandText = """
                    INSERT INTO projects
                        (project_id, source_id, site_project_id, title, url, client_name,
                         publish_time_number, publish_time_text,
                         proposal_count, proposal_count_text, description, budget, delivery_days,
                         project_status, owner_id, enrichment_status, discovered_at, enriched_at)
                    VALUES
                        (@project_id, @source_id, @site_project_id, @title, @url, @client_name,
                         @publish_time_number, @publish_time_text,
                         @proposal_count, @proposal_count_text, @description, @budget, @delivery_days,
                         @project_status, @owner_id, @enrichment_status, @discovered_at, @enriched_at)
                    ON CONFLICT(project_id) DO UPDATE SET
                        title = excluded.title,
                        url = excluded.url,
                        client_name = excluded.client_name,
                        publish_time_number = CASE
                            WHEN excluded.publish_time_number = 0
                            THEN projects.publish_time_number
                            ELSE excluded.publish_time_number
                        END,
                        publish_time_text = CASE
                            WHEN excluded.publish_time_text = ''
                            THEN projects.publish_time_text
                            ELSE excluded.publish_time_text
                        END,
                        -- The listing is the authoritative source for proposal counts. Some
                        -- sources' detail pages have no proposal-count row, which the parser
                        -- represents as 0. Do not erase a nonzero listing snapshot in that case.
                        proposal_count = CASE
                            WHEN excluded.proposal_count = 0
                            THEN projects.proposal_count
                            ELSE excluded.proposal_count
                        END,
                        proposal_count_text = CASE
                            WHEN excluded.proposal_count_text = ''
                            THEN projects.proposal_count_text
                            ELSE excluded.proposal_count_text
                        END,
                        description = excluded.description,
                        budget = excluded.budget,
                        delivery_days = excluded.delivery_days,
                        project_status = excluded.project_status,
                        owner_id = excluded.owner_id,
                        enrichment_status = excluded.enrichment_status,
                        enriched_at = excluded.enriched_at;
                    """;
                upsertCommand.Parameters.AddWithValue("@project_id", internalId);
                upsertCommand.Parameters.AddWithValue("@source_id", details.Source.Value);
                upsertCommand.Parameters.AddWithValue("@site_project_id", details.ProjectId);
                upsertCommand.Parameters.AddWithValue("@title", details.Title);
                upsertCommand.Parameters.AddWithValue("@url", details.Url);
                upsertCommand.Parameters.AddWithValue("@client_name", details.Owner.Name);
                upsertCommand.Parameters.AddWithValue("@publish_time_number", details.PublishTimeNumber);
                upsertCommand.Parameters.AddWithValue("@publish_time_text", details.PublishTimeText);
                upsertCommand.Parameters.AddWithValue("@proposal_count", details.ProposalCount);
                upsertCommand.Parameters.AddWithValue("@proposal_count_text", details.ProposalCountText);
                upsertCommand.Parameters.AddWithValue("@description", details.Description);
                upsertCommand.Parameters.AddWithValue("@budget", details.Budget is null ? DBNull.Value : details.Budget);
                upsertCommand.Parameters.AddWithValue("@delivery_days", details.DeliveryDays is null ? DBNull.Value : details.DeliveryDays);
                upsertCommand.Parameters.AddWithValue("@project_status", details.ProjectStatus is null ? DBNull.Value : details.ProjectStatus);
                upsertCommand.Parameters.AddWithValue("@owner_id", internalOwnerId is null ? DBNull.Value : internalOwnerId);
                upsertCommand.Parameters.AddWithValue("@enrichment_status", details.EnrichmentStatus.ToString());
                upsertCommand.Parameters.AddWithValue("@discovered_at", details.DiscoveredAt != default ? details.DiscoveredAt.ToString("O") : DateTimeOffset.UtcNow.ToString("O"));
                upsertCommand.Parameters.AddWithValue("@enriched_at", details.EnrichedAt is null ? DBNull.Value : details.EnrichedAt.Value.ToString("O"));
                await upsertCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            using (var deleteSkillsCommand = connection.CreateCommand())
            {
                deleteSkillsCommand.Transaction = transaction;
                deleteSkillsCommand.CommandText = "DELETE FROM project_skills WHERE project_id = @project_id;";
                deleteSkillsCommand.Parameters.AddWithValue("@project_id", internalId);
                await deleteSkillsCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            foreach (var skill in details.Skills)
            {
                using var skillCommand = connection.CreateCommand();
                skillCommand.Transaction = transaction;
                skillCommand.CommandText = """
                    INSERT INTO project_skills (project_id, name, url)
                    VALUES (@project_id, @name, @url);
                    """;
                skillCommand.Parameters.AddWithValue("@project_id", internalId);
                skillCommand.Parameters.AddWithValue("@name", skill.Name);
                skillCommand.Parameters.AddWithValue("@url", skill.Url is null ? DBNull.Value : skill.Url);
                await skillCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            using (var deleteAssetsCommand = connection.CreateCommand())
            {
                deleteAssetsCommand.Transaction = transaction;
                deleteAssetsCommand.CommandText = "DELETE FROM assets WHERE project_id = @project_id;";
                deleteAssetsCommand.Parameters.AddWithValue("@project_id", internalId);
                await deleteAssetsCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            foreach (var asset in details.Attachments)
            {
                using var assetCommand = connection.CreateCommand();
                assetCommand.Transaction = transaction;
                // Metadata only - no binary content is ever stored in the DB itself.
                assetCommand.CommandText = """
                    INSERT INTO assets
                        (project_id, file_name, url, raw_url, local_path, size_bytes, extension, requires_auth, size_text)
                    VALUES
                        (@project_id, @file_name, @url, @raw_url, @local_path, @size_bytes, @extension, @requires_auth, @size_text);
                    """;
                assetCommand.Parameters.AddWithValue("@project_id", internalId);
                assetCommand.Parameters.AddWithValue("@file_name", asset.FileName);
                assetCommand.Parameters.AddWithValue("@url", asset.Url);
                assetCommand.Parameters.AddWithValue("@raw_url", asset.RawUrl is null ? DBNull.Value : asset.RawUrl);
                assetCommand.Parameters.AddWithValue("@local_path", asset.LocalPath is null ? DBNull.Value : asset.LocalPath);
                assetCommand.Parameters.AddWithValue("@size_bytes", asset.SizeBytes is null ? DBNull.Value : asset.SizeBytes);
                assetCommand.Parameters.AddWithValue("@extension", asset.Extension is null ? DBNull.Value : asset.Extension);
                assetCommand.Parameters.AddWithValue("@requires_auth", asset.RequiresAuth ? 1 : 0);
                assetCommand.Parameters.AddWithValue("@size_text", asset.SizeText is null ? DBNull.Value : asset.SizeText);
                await assetCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            var skillsText = string.Join(" ", details.Skills.Select(s => s.Name));
            using (var deleteFtsCommand = connection.CreateCommand())
            {
                deleteFtsCommand.Transaction = transaction;
                deleteFtsCommand.CommandText = "DELETE FROM projects_fts WHERE project_id = @project_id;";
                deleteFtsCommand.Parameters.AddWithValue("@project_id", internalId);
                await deleteFtsCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            using (var insertFtsCommand = connection.CreateCommand())
            {
                insertFtsCommand.Transaction = transaction;
                insertFtsCommand.CommandText = """
                    INSERT INTO projects_fts (project_id, title, description, skills)
                    VALUES (@project_id, @title, @description, @skills);
                    """;
                insertFtsCommand.Parameters.AddWithValue("@project_id", internalId);
                insertFtsCommand.Parameters.AddWithValue("@title", details.Title);
                insertFtsCommand.Parameters.AddWithValue("@description", details.Description);
                insertFtsCommand.Parameters.AddWithValue("@skills", skillsText);
                await insertFtsCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            return Result<bool>.Ok(true);
        }
        catch (SqliteException ex)
        {
            InteractionLogger.Failure(nameof(UpsertDetailsAsync), DatabaseErrors.QueryFailed(nameof(UpsertDetailsAsync), ex), new { details.ProjectId, ex.Message, ex.StackTrace });
            return Result<bool>.Err(DatabaseErrors.QueryFailed(nameof(UpsertDetailsAsync), ex));
        }
        catch (Exception ex)
        {
            InteractionLogger.Fault(nameof(UpsertDetailsAsync), ex, new { details.ProjectId });
            throw;
        }
    }

    [ErrorOutcome(ErrorOutcome.Handled, Label = "Query failure surfaced as Result.Err")]
    public async Task<Result<IReadOnlySet<SourceProjectId>>> GetAllKnownProjectIdsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = _connectionFactory.CreateConnection();
            const string sql = "SELECT source_id, project_id FROM projects;";

            var ids = new HashSet<SourceProjectId>();
            var rows = await connection.QueryAsync<RowMaps.ProjectKeyRow>(
                new CommandDefinition(sql, cancellationToken: cancellationToken));
            foreach (var row in rows)
            {
                ids.Add(KeyFromRow(row.source_id, row.project_id));
            }

            return Result<IReadOnlySet<SourceProjectId>>.Ok(ids);
        }
        catch (SqliteException ex)
        {
            return Result<IReadOnlySet<SourceProjectId>>.Err(DatabaseErrors.QueryFailed(nameof(GetAllKnownProjectIdsAsync), ex));
        }
    }

    [ErrorOutcome(ErrorOutcome.Handled, Label = "Query failure surfaced as Result.Err")]
    public async Task<Result<IReadOnlyList<ProjectSummary>>> GetRecentAsync(SourceId source, int limit, CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = _connectionFactory.CreateConnection();
            const string sql = """
                SELECT p.project_id, p.source_id, p.title, p.url, p.client_name,
                       p.publish_time_number, p.publish_time_text,
                       p.proposal_count, p.proposal_count_text,
                       p.description, p.budget, p.delivery_days,
                       p.is_unread, p.enrichment_status, p.discovered_at, p.project_status,
                       COALESCE((SELECT group_concat(name, ', ') FROM project_skills s WHERE s.project_id = p.project_id), '') AS skills_text,
                       p.enriched_at
                FROM projects p
                WHERE p.source_id = @source_id
                -- Sort by enrichment *completion*, not discovery time: the most recently fully
                -- enriched project must land at the top, while projects still pending/being
                -- enriched (enriched_at IS NULL) stay below every completed one, ordered among
                -- themselves by discovery recency. Two rows completing at the "same" instant are
                -- simply ordered by their (distinct, high-precision "O"-formatted) enriched_at
                -- values - no special tie-break is needed, and neither completion blocks or
                -- overwrites the other since each is an independent UPDATE.
                ORDER BY (p.enriched_at IS NULL) ASC, p.enriched_at DESC, p.discovered_at DESC
                LIMIT @limit;
                """;
            var rows = await connection.QueryAsync<RowMaps.ProjectSummaryRow>(
                new CommandDefinition(sql, new { source_id = source.Value, limit }, cancellationToken: cancellationToken));

            var results = new List<ProjectSummary>();
            foreach (var row in rows)
            {
                results.Add(row.ToSummary(_registry));
            }

            return Result<IReadOnlyList<ProjectSummary>>.Ok(results);
        }
        catch (SqliteException ex)
        {
            return Result<IReadOnlyList<ProjectSummary>>.Err(DatabaseErrors.QueryFailed(nameof(GetRecentAsync), ex));
        }
    }

    [ErrorOutcome(ErrorOutcome.Handled, Label = "Query failure surfaced as Result.Err")]
    public async Task<Result<SourceProjectId?>> GetNewestProjectIdAsync(SourceId source, CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = _connectionFactory.CreateConnection();
            // The details page is the only caller: it needs a row that actually has a description,
            // budget, skills and attachments, so enriched rows win over a newer-but-bare discovery,
            // and among those the most recently enriched one is shown.
            const string sql = """
                SELECT source_id, project_id
                FROM projects
                WHERE source_id = @source_id
                ORDER BY (enrichment_status = 'Enriched') DESC,
                         COALESCE(enriched_at, discovered_at) DESC
                LIMIT 1;
                """;
            var row = await connection.QueryFirstOrDefaultAsync<RowMaps.ProjectKeyRow>(
                new CommandDefinition(sql, new { source_id = source.Value }, cancellationToken: cancellationToken));
            if (row is null)
            {
                return Result<SourceProjectId?>.Ok(null);
            }

            return Result<SourceProjectId?>.Ok(KeyFromRow(row.source_id, row.project_id));
        }
        catch (SqliteException ex)
        {
            return Result<SourceProjectId?>.Err(DatabaseErrors.QueryFailed(nameof(GetNewestProjectIdAsync), ex));
        }
    }

    [ErrorOutcome(ErrorOutcome.Handled, Label = "Query failure surfaced as Result.Err")]
    public async Task<Result<ProjectDetails?>> GetDetailsAsync(SourceProjectId projectId, CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = _connectionFactory.CreateConnection();
            var internalId = InternalId(projectId);

            const string detailsSql = """
                    SELECT p.project_id, p.source_id, p.title, p.url, p.description, p.budget, p.delivery_days,
                           p.enrichment_status, p.enriched_at, p.project_status,
                           p.publish_time_number, p.publish_time_text,
                           p.proposal_count, p.proposal_count_text, p.discovered_at,
                           o.owner_id, o.name, o.profile_url, o.avatar_url, o.rating,
                           o.completed_projects_count, o.hiring_rate_percent,
                           o.registered_at, o.open_projects_count, o.in_progress_projects_count,
                           o.ongoing_communications_count
                    FROM projects p
                    LEFT JOIN owners o ON o.owner_id = p.owner_id
                    WHERE p.project_id = @project_id;
                    """;
            var detailsRow = await connection.QueryFirstOrDefaultAsync<RowMaps.ProjectDetailsRow>(
                new CommandDefinition(detailsSql, new { project_id = internalId }, cancellationToken: cancellationToken));
            ProjectDetails? details = detailsRow?.ToDetails(_registry);

            if (details is null)
            {
                return Result<ProjectDetails?>.Ok(null);
            }

            var skillRows = await connection.QueryAsync<RowMaps.SkillRow>(
                new CommandDefinition("SELECT name, url FROM project_skills WHERE project_id = @project_id;",
                    new { project_id = internalId }, cancellationToken: cancellationToken));
            foreach (var skillRow in skillRows)
            {
                details.Skills.Add(new ProjectSkill
                {
                    Name = skillRow.name,
                    Url = skillRow.url,
                });
            }

            var assetRows = await connection.QueryAsync<RowMaps.AssetRow>(
                new CommandDefinition("""
                    SELECT asset_id, project_id, file_name, url, raw_url, local_path, size_bytes,
                           extension, requires_auth, size_text
                    FROM assets
                    WHERE project_id = @project_id;
                    """,
                    new { project_id = internalId }, cancellationToken: cancellationToken));
            foreach (var assetRow in assetRows)
            {
                details.Attachments.Add(assetRow.ToAsset());
            }

            return Result<ProjectDetails?>.Ok(details);
        }
        catch (SqliteException ex)
        {
            return Result<ProjectDetails?>.Err(DatabaseErrors.QueryFailed(nameof(GetDetailsAsync), ex));
        }
    }

    [ErrorOutcome(ErrorOutcome.Handled, Label = "Query failure surfaced as Result<int>.Err")]
    public async Task<Result<int>> CountAddedTodayAsync(SourceId source, CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = _connectionFactory.CreateConnection();
            const string sql = "SELECT COUNT(*) FROM projects WHERE source_id = @source_id AND date(discovered_at) = date('now');";

            var result = await connection.ExecuteScalarAsync<object>(
                new CommandDefinition(sql, new { source_id = source.Value }, cancellationToken: cancellationToken));
            var count = result is null || result is DBNull ? 0 : Convert.ToInt32(result);
            return Result<int>.Ok(count);
        }
        catch (SqliteException ex)
        {
            return Result<int>.Err(DatabaseErrors.QueryFailed(nameof(CountAddedTodayAsync), ex));
        }
    }

    [ErrorOutcome(ErrorOutcome.Handled, Label = "Command failure surfaced as Result.Err")]
    public async Task<Result<bool>> AddToBacklogAsync(SourceProjectId projectId, CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = _connectionFactory.CreateConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "INSERT OR IGNORE INTO discovery_backlog (project_id, source_id) VALUES (@projectId, @source_id);";
            command.Parameters.AddWithValue("@projectId", InternalId(projectId));
            command.Parameters.AddWithValue("@source_id", projectId.Source.Value);

            await command.ExecuteNonQueryAsync(cancellationToken);
            return Result<bool>.Ok(true);
        }
        catch (SqliteException ex)
        {
            return Result<bool>.Err(DatabaseErrors.CommandFailed(nameof(AddToBacklogAsync), ex));
        }
    }

    [ErrorOutcome(ErrorOutcome.Handled, Label = "Command failure surfaced as Result.Err")]
    public async Task<Result<bool>> RemoveFromBacklogAsync(SourceProjectId projectId, CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = _connectionFactory.CreateConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM discovery_backlog WHERE project_id = @projectId;";
            command.Parameters.AddWithValue("@projectId", InternalId(projectId));

            await command.ExecuteNonQueryAsync(cancellationToken);
            return Result<bool>.Ok(true);
        }
        catch (SqliteException ex)
        {
            return Result<bool>.Err(DatabaseErrors.CommandFailed(nameof(RemoveFromBacklogAsync), ex));
        }
    }

    [ErrorOutcome(ErrorOutcome.Handled, Label = "Query failure surfaced as Result.Err")]
    public async Task<Result<IReadOnlyList<SourceProjectId>>> GetBacklogIdsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = _connectionFactory.CreateConnection();
            const string sql = "SELECT source_id, project_id FROM discovery_backlog ORDER BY discovered_at ASC;";

            var ids = new List<SourceProjectId>();
            var rows = await connection.QueryAsync<RowMaps.ProjectKeyRow>(
                new CommandDefinition(sql, cancellationToken: cancellationToken));
            foreach (var row in rows)
            {
                ids.Add(KeyFromRow(row.source_id, row.project_id));
            }
            return Result<IReadOnlyList<SourceProjectId>>.Ok(ids);
        }
        catch (SqliteException ex)
        {
            return Result<IReadOnlyList<SourceProjectId>>.Err(DatabaseErrors.QueryFailed(nameof(GetBacklogIdsAsync), ex));
        }
    }

    [ErrorOutcome(ErrorOutcome.Handled, Label = "Command failure surfaced as Result.Err")]
    public async Task<Result<int>> CleanOldBacklogAsync(int days = 30, CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = _connectionFactory.CreateConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM discovery_backlog WHERE discovered_at < datetime('now', '-' || @days || ' days');";
            command.Parameters.AddWithValue("@days", days);

            var count = await command.ExecuteNonQueryAsync(cancellationToken);
            return Result<int>.Ok(count);
        }
        catch (SqliteException ex)
        {
            return Result<int>.Err(DatabaseErrors.CommandFailed(nameof(CleanOldBacklogAsync), ex));
        }
    }

    [ErrorOutcome(ErrorOutcome.Handled, Label = "Query failure surfaced as Result.Err")]
    public async Task<Result<(int Tracked, int Unread)>> CountTrackedAsync(SourceId source, CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = _connectionFactory.CreateConnection();
            // Aliases exist purely so Dapper can bind the two unnamed aggregates by name;
            // the returned values/order are unchanged.
            const string sql = "SELECT COUNT(*) AS tracked, COALESCE(SUM(is_unread), 0) AS unread FROM projects WHERE source_id = @source_id;";

            var row = await connection.QueryFirstOrDefaultAsync<RowMaps.CountersRow>(
                new CommandDefinition(sql, new { source_id = source.Value }, cancellationToken: cancellationToken));
            if (row is null)
            {
                return Result<(int, int)>.Ok((0, 0));
            }

            return Result<(int, int)>.Ok((row.tracked, row.unread));
        }
        catch (SqliteException ex)
        {
            return Result<(int, int)>.Err(DatabaseErrors.QueryFailed(nameof(CountTrackedAsync), ex));
        }
    }

    [ErrorOutcome(ErrorOutcome.Handled, Label = "Command failure surfaced as Result<bool>.Err")]
    public async Task<Result<bool>> MarkAsReadAsync(SourceProjectId projectId, CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = _connectionFactory.CreateConnection();
            using var command = connection.CreateCommand();
            // Local read-state, not scraped content - updating it does not violate the
            // no-update/store-and-forget policy that governs the scraped project fields.
            command.CommandText = "UPDATE projects SET is_unread = 0 WHERE project_id = @project_id AND is_unread = 1;";
            command.Parameters.AddWithValue("@project_id", InternalId(projectId));

            await command.ExecuteNonQueryAsync(cancellationToken);
            return Result<bool>.Ok(true);
        }
        catch (SqliteException ex)
        {
            return Result<bool>.Err(DatabaseErrors.CommandFailed(nameof(MarkAsReadAsync), ex));
        }
    }

    [ErrorOutcome(ErrorOutcome.Handled, Label = "Command failure surfaced as Result<bool>.Err")]
    public async Task<Result<bool>> MarkAllAsReadAsync(SourceId source, CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = _connectionFactory.CreateConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE projects SET is_unread = 0 WHERE is_unread = 1 AND source_id = @source_id;";
            command.Parameters.AddWithValue("@source_id", source.Value);

            await command.ExecuteNonQueryAsync(cancellationToken);
            return Result<bool>.Ok(true);
        }
        catch (SqliteException ex)
        {
            return Result<bool>.Err(DatabaseErrors.CommandFailed(nameof(MarkAllAsReadAsync), ex));
        }
    }

    [ErrorOutcome(ErrorOutcome.Handled, Label = "Query failure surfaced as Result<bool>.Err")]
    public async Task<Result<bool>> ClearAllAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = _connectionFactory.CreateConnection();
            using var transaction = connection.BeginTransaction();
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = """
                    DELETE FROM assets;
                    DELETE FROM project_skills;
                    DELETE FROM projects_fts;
                    DELETE FROM projects;
                    """;
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            return Result<bool>.Ok(true);
        }
        catch (SqliteException ex)
        {
            return Result<bool>.Err(DatabaseErrors.QueryFailed(nameof(ClearAllAsync), ex));
        }
    }

    [ErrorOutcome(ErrorOutcome.Handled, Label = "Query failure surfaced as Result<int>.Err")]
    public async Task<Result<int>> DeleteByProjectIdRangeAsync(SourceId source, long minSiteProjectId, long maxSiteProjectId, CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = _connectionFactory.CreateConnection();
            using var transaction = connection.BeginTransaction();

            int rowsAffected;
            using (var deleteAssetsCommand = connection.CreateCommand())
            {
                deleteAssetsCommand.Transaction = transaction;
                deleteAssetsCommand.CommandText = """
                    DELETE FROM assets
                    WHERE project_id IN (SELECT project_id FROM projects WHERE source_id = @source AND site_project_id BETWEEN @min_id AND @max_id);
                    """;
                deleteAssetsCommand.Parameters.AddWithValue("@source", source.Value);
                deleteAssetsCommand.Parameters.AddWithValue("@min_id", minSiteProjectId);
                deleteAssetsCommand.Parameters.AddWithValue("@max_id", maxSiteProjectId);
                await deleteAssetsCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            using (var deleteSkillsCommand = connection.CreateCommand())
            {
                deleteSkillsCommand.Transaction = transaction;
                deleteSkillsCommand.CommandText = """
                    DELETE FROM project_skills
                    WHERE project_id IN (SELECT project_id FROM projects WHERE source_id = @source AND site_project_id BETWEEN @min_id AND @max_id);
                    """;
                deleteSkillsCommand.Parameters.AddWithValue("@source", source.Value);
                deleteSkillsCommand.Parameters.AddWithValue("@min_id", minSiteProjectId);
                deleteSkillsCommand.Parameters.AddWithValue("@max_id", maxSiteProjectId);
                await deleteSkillsCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            using (var deleteFtsCommand = connection.CreateCommand())
            {
                deleteFtsCommand.Transaction = transaction;
                deleteFtsCommand.CommandText = """
                    DELETE FROM projects_fts
                    WHERE project_id IN (SELECT project_id FROM projects WHERE source_id = @source AND site_project_id BETWEEN @min_id AND @max_id);
                    """;
                deleteFtsCommand.Parameters.AddWithValue("@source", source.Value);
                deleteFtsCommand.Parameters.AddWithValue("@min_id", minSiteProjectId);
                deleteFtsCommand.Parameters.AddWithValue("@max_id", maxSiteProjectId);
                await deleteFtsCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            using (var deleteProjectsCommand = connection.CreateCommand())
            {
                deleteProjectsCommand.Transaction = transaction;
                deleteProjectsCommand.CommandText = "DELETE FROM projects WHERE source_id = @source AND site_project_id BETWEEN @min_id AND @max_id;";
                deleteProjectsCommand.Parameters.AddWithValue("@source", source.Value);
                deleteProjectsCommand.Parameters.AddWithValue("@min_id", minSiteProjectId);
                deleteProjectsCommand.Parameters.AddWithValue("@max_id", maxSiteProjectId);
                rowsAffected = await deleteProjectsCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            return Result<int>.Ok(rowsAffected);
        }
        catch (SqliteException ex)
        {
            return Result<int>.Err(DatabaseErrors.QueryFailed(nameof(DeleteByProjectIdRangeAsync), ex));
        }
    }

    [ErrorOutcome(ErrorOutcome.Handled, Label = "Query failure surfaced as Result.Err")]
    public async Task<Result<IReadOnlyList<ProjectDetails>>> GetAllDetailsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = _connectionFactory.CreateConnection();
            var results = new List<ProjectDetails>();
            var projectMap = new Dictionary<long, ProjectDetails>();

            const string detailsSql = """
                    SELECT p.project_id, p.source_id, p.title, p.url, p.description, p.budget, p.delivery_days,
                           p.enrichment_status, p.enriched_at, p.project_status,
                           p.publish_time_number, p.publish_time_text,
                           p.proposal_count, p.proposal_count_text, p.discovered_at,
                           o.owner_id, o.name, o.profile_url, o.avatar_url, o.rating,
                           o.completed_projects_count, o.hiring_rate_percent,
                           o.registered_at, o.open_projects_count, o.in_progress_projects_count,
                           o.ongoing_communications_count
                    FROM projects p
                    LEFT JOIN owners o ON o.owner_id = p.owner_id;
                    """;

            var rows = await connection.QueryAsync<RowMaps.ProjectDetailsRow>(
                new CommandDefinition(detailsSql, cancellationToken: cancellationToken));
            foreach (var row in rows)
            {
                var details = row.ToDetails(_registry);
                results.Add(details);
                // Batch fetches below join on the internal row id, which is what child tables store.
                projectMap[row.project_id] = details;
            }

            if (results.Count > 0)
            {
                // Batch fetch skills and attachments in single queries to avoid N+1 queries
                var skillRows = await connection.QueryAsync<RowMaps.SkillRow>(
                    new CommandDefinition("SELECT project_id, name, url FROM project_skills;",
                        cancellationToken: cancellationToken));
                foreach (var skillRow in skillRows)
                {
                    if (projectMap.TryGetValue(skillRow.project_id, out var details))
                    {
                        details.Skills.Add(new ProjectSkill
                        {
                            Name = skillRow.name,
                            Url = skillRow.url,
                        });
                    }
                }

                var assetRows = await connection.QueryAsync<RowMaps.AssetRow>(
                    new CommandDefinition("""
                        SELECT asset_id, project_id, file_name, url, raw_url, local_path, size_bytes,
                               extension, requires_auth, size_text
                        FROM assets;
                        """, cancellationToken: cancellationToken));
                foreach (var assetRow in assetRows)
                {
                    var asset = assetRow.ToAsset();
                    if (projectMap.TryGetValue(asset.ProjectId, out var details))
                    {
                        details.Attachments.Add(asset);
                    }
                }
            }

            return Result<IReadOnlyList<ProjectDetails>>.Ok(results);
        }
        catch (SqliteException ex)
        {
            return Result<IReadOnlyList<ProjectDetails>>.Err(DatabaseErrors.QueryFailed(nameof(GetAllDetailsAsync), ex));
        }
    }

}
