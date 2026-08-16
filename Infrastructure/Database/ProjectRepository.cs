using Microsoft.Data.Sqlite;
using MostaqlK.Core;
using MostaqlK.Models;
using MostaqlK.Services.Diagnostics;

namespace MostaqlK.Infrastructure.Database;

/// <inheritdoc cref="IProjectRepository"/>
public sealed class ProjectRepository : IProjectRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;

    public ProjectRepository(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    [ErrorOutcome(ErrorOutcome.Handled, Label = "Query failure surfaced as Result<bool>.Err")]
    public async Task<Result<bool>> InsertSummaryAsync(ProjectSummary project, CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = _connectionFactory.CreateConnection();
            using var transaction = connection.BeginTransaction();

            int rowsAffected;
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                // Write-once: a project row is never overwritten once it exists (no-update policy).
                command.CommandText = """
                    INSERT OR IGNORE INTO projects
                        (project_id, title, url, client_name, publish_time_number, publish_time_text,
                         proposal_count, proposal_count_text, is_unread, enrichment_status, discovered_at)
                    VALUES
                        (@project_id, @title, @url, @client_name, @publish_time_number, @publish_time_text,
                         @proposal_count, @proposal_count_text, @is_unread, @enrichment_status, @discovered_at);
                    """;
                command.Parameters.AddWithValue("@project_id", project.ProjectId);
                command.Parameters.AddWithValue("@title", project.Title);
                command.Parameters.AddWithValue("@url", project.Url);
                command.Parameters.AddWithValue("@client_name", project.ClientName);
                command.Parameters.AddWithValue("@publish_time_number", project.PublishTimeNumber);
                command.Parameters.AddWithValue("@publish_time_text", project.PublishTimeText);
                command.Parameters.AddWithValue("@proposal_count", project.ProposalCount);
                command.Parameters.AddWithValue("@proposal_count_text", project.ProposalCountText);
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
                    VALUES (@project_id, @title, '', '');
                    """;
                insertFtsCommand.Parameters.AddWithValue("@project_id", project.ProjectId);
                insertFtsCommand.Parameters.AddWithValue("@title", project.Title);
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

            // The one exception to the write-once policy: completing a previously
            // "Pending" row with the enriched fields fetched from the detail page.
            // Fields not yet known at discovery time (description/budget/etc.) are
            // filled in here; the project_id itself is never duplicated.
            using (var upsertCommand = connection.CreateCommand())
            {
                upsertCommand.Transaction = transaction;
                upsertCommand.CommandText = """
                    INSERT INTO projects
                        (project_id, title, url, client_name, description, budget, delivery_days,
                         project_status, publish_time_number, publish_time_text,
                         proposal_count, proposal_count_text,
                         owner_id, enrichment_status, discovered_at, enriched_at)
                    VALUES
                        (@project_id, @title, @url, @client_name, @description, @budget, @delivery_days,
                         @project_status, @publish_time_number, @publish_time_text,
                         @proposal_count, @proposal_count_text,
                         @owner_id, @enrichment_status, @discovered_at, @enriched_at)
                    ON CONFLICT(project_id) DO UPDATE SET
                        title = excluded.title,
                        url = excluded.url,
                        client_name = excluded.client_name,
                        description = excluded.description,
                        budget = excluded.budget,
                        delivery_days = excluded.delivery_days,
                        project_status = excluded.project_status,
                        publish_time_number = excluded.publish_time_number,
                        publish_time_text = excluded.publish_time_text,
                        proposal_count = excluded.proposal_count,
                        proposal_count_text = excluded.proposal_count_text,
                        owner_id = excluded.owner_id,
                        enrichment_status = excluded.enrichment_status,
                        enriched_at = excluded.enriched_at;
                    """;
                upsertCommand.Parameters.AddWithValue("@project_id", details.ProjectId);
                upsertCommand.Parameters.AddWithValue("@title", details.Title);
                upsertCommand.Parameters.AddWithValue("@url", details.Url);
                upsertCommand.Parameters.AddWithValue("@client_name", details.Owner.Name);
                upsertCommand.Parameters.AddWithValue("@description", details.Description);
                upsertCommand.Parameters.AddWithValue("@budget", details.Budget is null ? DBNull.Value : details.Budget);
                upsertCommand.Parameters.AddWithValue("@delivery_days", details.DeliveryDays is null ? DBNull.Value : details.DeliveryDays);
                upsertCommand.Parameters.AddWithValue("@project_status", details.ProjectStatus is null ? DBNull.Value : details.ProjectStatus);
                upsertCommand.Parameters.AddWithValue("@publish_time_number", details.PublishTimeNumber);
                upsertCommand.Parameters.AddWithValue("@publish_time_text", details.PublishTimeText);
                upsertCommand.Parameters.AddWithValue("@proposal_count", details.ProposalCount);
                upsertCommand.Parameters.AddWithValue("@proposal_count_text", details.ProposalCountText);
                upsertCommand.Parameters.AddWithValue("@owner_id", details.Owner.OwnerId == 0 ? DBNull.Value : details.Owner.OwnerId);
                upsertCommand.Parameters.AddWithValue("@enrichment_status", details.EnrichmentStatus.ToString());
                upsertCommand.Parameters.AddWithValue("@discovered_at", DateTimeOffset.UtcNow.ToString("O"));
                upsertCommand.Parameters.AddWithValue("@enriched_at", details.EnrichedAt is null ? DBNull.Value : details.EnrichedAt.Value.ToString("O"));
                await upsertCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            using (var deleteSkillsCommand = connection.CreateCommand())
            {
                deleteSkillsCommand.Transaction = transaction;
                deleteSkillsCommand.CommandText = "DELETE FROM project_skills WHERE project_id = @project_id;";
                deleteSkillsCommand.Parameters.AddWithValue("@project_id", details.ProjectId);
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
                skillCommand.Parameters.AddWithValue("@project_id", details.ProjectId);
                skillCommand.Parameters.AddWithValue("@name", skill.Name);
                skillCommand.Parameters.AddWithValue("@url", skill.Url is null ? DBNull.Value : skill.Url);
                await skillCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            using (var deleteAssetsCommand = connection.CreateCommand())
            {
                deleteAssetsCommand.Transaction = transaction;
                deleteAssetsCommand.CommandText = "DELETE FROM assets WHERE project_id = @project_id;";
                deleteAssetsCommand.Parameters.AddWithValue("@project_id", details.ProjectId);
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
                assetCommand.Parameters.AddWithValue("@project_id", details.ProjectId);
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
                deleteFtsCommand.Parameters.AddWithValue("@project_id", details.ProjectId);
                await deleteFtsCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            using (var insertFtsCommand = connection.CreateCommand())
            {
                insertFtsCommand.Transaction = transaction;
                insertFtsCommand.CommandText = """
                    INSERT INTO projects_fts (project_id, title, description, skills)
                    VALUES (@project_id, @title, @description, @skills);
                    """;
                insertFtsCommand.Parameters.AddWithValue("@project_id", details.ProjectId);
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
    public async Task<Result<IReadOnlySet<long>>> GetAllKnownProjectIdsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = _connectionFactory.CreateConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT project_id FROM projects;";

            var ids = new HashSet<long>();
            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                ids.Add(reader.GetInt64(0));
            }

            return Result<IReadOnlySet<long>>.Ok(ids);
        }
        catch (SqliteException ex)
        {
            return Result<IReadOnlySet<long>>.Err(DatabaseErrors.QueryFailed(nameof(GetAllKnownProjectIdsAsync), ex));
        }
    }

    [ErrorOutcome(ErrorOutcome.Handled, Label = "Query failure surfaced as Result.Err")]
    public async Task<Result<IReadOnlyList<ProjectSummary>>> GetRecentAsync(int limit, CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = _connectionFactory.CreateConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT p.project_id, p.title, p.url, p.client_name, p.publish_time_number, p.publish_time_text,
                       p.proposal_count, p.proposal_count_text, p.description, p.budget, p.delivery_days,
                       p.is_unread, p.enrichment_status, p.discovered_at, p.project_status,
                       COALESCE((SELECT group_concat(name, ', ') FROM project_skills s WHERE s.project_id = p.project_id), '')
                FROM projects p
                ORDER BY p.discovered_at DESC
                LIMIT @limit;
                """;
            command.Parameters.AddWithValue("@limit", limit);

            var results = new List<ProjectSummary>();
            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                results.Add(ReadSummary(reader));
            }

            return Result<IReadOnlyList<ProjectSummary>>.Ok(results);
        }
        catch (SqliteException ex)
        {
            return Result<IReadOnlyList<ProjectSummary>>.Err(DatabaseErrors.QueryFailed(nameof(GetRecentAsync), ex));
        }
    }

    [ErrorOutcome(ErrorOutcome.Handled, Label = "Query failure surfaced as Result.Err")]
    public async Task<Result<long?>> GetNewestProjectIdAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = _connectionFactory.CreateConnection();
            using var command = connection.CreateCommand();
            // The details page is the only caller: it needs a row that actually has a description,
            // budget, skills and attachments, so enriched rows win over a newer-but-bare discovery,
            // and among those the most recently enriched one is shown.
            command.CommandText = """
                SELECT project_id
                FROM projects
                ORDER BY (enrichment_status = 'Enriched') DESC,
                         COALESCE(enriched_at, discovered_at) DESC
                LIMIT 1;
                """;
            var value = await command.ExecuteScalarAsync(cancellationToken);
            return Result<long?>.Ok(value is null || value is DBNull ? null : Convert.ToInt64(value));
        }
        catch (SqliteException ex)
        {
            return Result<long?>.Err(DatabaseErrors.QueryFailed(nameof(GetNewestProjectIdAsync), ex));
        }
    }

    [ErrorOutcome(ErrorOutcome.Handled, Label = "Query failure surfaced as Result.Err")]
    public async Task<Result<ProjectDetails?>> GetDetailsAsync(long projectId, CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = _connectionFactory.CreateConnection();

            ProjectDetails? details = null;
            using (var command = connection.CreateCommand())
            {
                command.CommandText = """
                    SELECT p.project_id, p.title, p.url, p.description, p.budget, p.delivery_days,
                           p.enrichment_status, p.enriched_at, p.project_status,
                           p.publish_time_number, p.publish_time_text,
                           p.proposal_count, p.proposal_count_text,
                           o.owner_id, o.name, o.profile_url, o.avatar_url, o.rating,
                           o.completed_projects_count, o.hiring_rate_percent,
                           o.registered_at, o.open_projects_count, o.in_progress_projects_count,
                           o.ongoing_communications_count
                    FROM projects p
                    LEFT JOIN owners o ON o.owner_id = p.owner_id
                    WHERE p.project_id = @project_id;
                    """;
                command.Parameters.AddWithValue("@project_id", projectId);

                using var reader = await command.ExecuteReaderAsync(cancellationToken);
                if (await reader.ReadAsync(cancellationToken))
                {
                    details = new ProjectDetails
                    {
                        ProjectId = reader.GetInt64(0),
                        Title = reader.GetString(1),
                        Url = reader.GetString(2),
                        Description = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                        Budget = reader.IsDBNull(4) ? null : reader.GetString(4),
                        DeliveryDays = reader.IsDBNull(5) ? null : reader.GetInt32(5),
                        EnrichmentStatus = Enum.Parse<EnrichmentStatus>(reader.GetString(6)),
                        EnrichedAt = reader.IsDBNull(7) ? null : DateTimeOffset.Parse(reader.GetString(7)),
                        ProjectStatus = reader.IsDBNull(8) ? null : reader.GetString(8),
                        PublishTimeNumber = reader.IsDBNull(9) ? 0 : reader.GetInt32(9),
                        PublishTimeText = reader.IsDBNull(10) ? string.Empty : reader.GetString(10),
                        ProposalCount = reader.IsDBNull(11) ? 0 : reader.GetInt32(11),
                        ProposalCountText = reader.IsDBNull(12) ? string.Empty : reader.GetString(12),
                        Owner = reader.IsDBNull(13)
                            ? new Owner()
                            : new Owner
                            {
                                OwnerId = reader.GetInt64(13),
                                Name = reader.IsDBNull(14) ? string.Empty : reader.GetString(14),
                                ProfileUrl = reader.IsDBNull(15) ? null : reader.GetString(15),
                                AvatarUrl = reader.IsDBNull(16) ? null : reader.GetString(16),
                                Rating = reader.IsDBNull(17) ? null : reader.GetDouble(17),
                                CompletedProjectsCount = reader.IsDBNull(18) ? null : reader.GetInt32(18),
                                HiringRatePercent = reader.IsDBNull(19) ? null : reader.GetInt32(19),
                                RegisteredAt = reader.IsDBNull(20) ? null : reader.GetString(20),
                                OpenProjectsCount = reader.IsDBNull(21) ? null : reader.GetInt32(21),
                                InProgressProjectsCount = reader.IsDBNull(22) ? null : reader.GetInt32(22),
                                OngoingCommunicationsCount = reader.IsDBNull(23) ? null : reader.GetInt32(23),
                            },
                    };
                }
            }

            if (details is null)
            {
                return Result<ProjectDetails?>.Ok(null);
            }

            using (var skillsCommand = connection.CreateCommand())
            {
                skillsCommand.CommandText = "SELECT name, url FROM project_skills WHERE project_id = @project_id;";
                skillsCommand.Parameters.AddWithValue("@project_id", projectId);
                using var reader = await skillsCommand.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    details.Skills.Add(new ProjectSkill
                    {
                        Name = reader.GetString(0),
                        Url = reader.IsDBNull(1) ? null : reader.GetString(1),
                    });
                }
            }

            using (var assetsCommand = connection.CreateCommand())
            {
                assetsCommand.CommandText = """
                    SELECT asset_id, project_id, file_name, url, raw_url, local_path, size_bytes,
                           extension, requires_auth, size_text
                    FROM assets
                    WHERE project_id = @project_id;
                    """;
                assetsCommand.Parameters.AddWithValue("@project_id", projectId);
                using var reader = await assetsCommand.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    details.Attachments.Add(ReadAsset(reader));
                }
            }

            return Result<ProjectDetails?>.Ok(details);
        }
        catch (SqliteException ex)
        {
            return Result<ProjectDetails?>.Err(DatabaseErrors.QueryFailed(nameof(GetDetailsAsync), ex));
        }
    }

    [ErrorOutcome(ErrorOutcome.Handled, Label = "Query failure surfaced as Result<int>.Err")]
    public async Task<Result<int>> CountAddedTodayAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = _connectionFactory.CreateConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM projects WHERE date(discovered_at) = date('now');";

            var result = await command.ExecuteScalarAsync(cancellationToken);
            var count = result is null || result is DBNull ? 0 : Convert.ToInt32(result);
            return Result<int>.Ok(count);
        }
        catch (SqliteException ex)
        {
            return Result<int>.Err(DatabaseErrors.QueryFailed(nameof(CountAddedTodayAsync), ex));
        }
    }

    [ErrorOutcome(ErrorOutcome.Handled, Label = "Command failure surfaced as Result.Err")]
    public async Task<Result<bool>> AddToBacklogAsync(long projectId, CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = _connectionFactory.CreateConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "INSERT OR IGNORE INTO discovery_backlog (project_id) VALUES (@projectId);";
            command.Parameters.AddWithValue("@projectId", projectId);

            await command.ExecuteNonQueryAsync(cancellationToken);
            return Result<bool>.Ok(true);
        }
        catch (SqliteException ex)
        {
            return Result<bool>.Err(DatabaseErrors.CommandFailed(nameof(AddToBacklogAsync), ex));
        }
    }

    [ErrorOutcome(ErrorOutcome.Handled, Label = "Command failure surfaced as Result.Err")]
    public async Task<Result<bool>> RemoveFromBacklogAsync(long projectId, CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = _connectionFactory.CreateConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM discovery_backlog WHERE project_id = @projectId;";
            command.Parameters.AddWithValue("@projectId", projectId);

            await command.ExecuteNonQueryAsync(cancellationToken);
            return Result<bool>.Ok(true);
        }
        catch (SqliteException ex)
        {
            return Result<bool>.Err(DatabaseErrors.CommandFailed(nameof(RemoveFromBacklogAsync), ex));
        }
    }

    [ErrorOutcome(ErrorOutcome.Handled, Label = "Query failure surfaced as Result.Err")]
    public async Task<Result<IReadOnlyList<long>>> GetBacklogIdsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = _connectionFactory.CreateConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT project_id FROM discovery_backlog ORDER BY discovered_at ASC;";

            var ids = new List<long>();
            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                ids.Add(reader.GetInt64(0));
            }
            return Result<IReadOnlyList<long>>.Ok(ids);
        }
        catch (SqliteException ex)
        {
            return Result<IReadOnlyList<long>>.Err(DatabaseErrors.QueryFailed(nameof(GetBacklogIdsAsync), ex));
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
    public async Task<Result<(int Tracked, int Unread)>> CountTrackedAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = _connectionFactory.CreateConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*), COALESCE(SUM(is_unread), 0) FROM projects;";

            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return Result<(int, int)>.Ok((0, 0));
            }

            return Result<(int, int)>.Ok((reader.GetInt32(0), reader.GetInt32(1)));
        }
        catch (SqliteException ex)
        {
            return Result<(int, int)>.Err(DatabaseErrors.QueryFailed(nameof(CountTrackedAsync), ex));
        }
    }

    [ErrorOutcome(ErrorOutcome.Handled, Label = "Command failure surfaced as Result<bool>.Err")]
    public async Task<Result<bool>> MarkAsReadAsync(long projectId, CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = _connectionFactory.CreateConnection();
            using var command = connection.CreateCommand();
            // Local read-state, not scraped content - updating it does not violate the
            // no-update/store-and-forget policy that governs the scraped project fields.
            command.CommandText = "UPDATE projects SET is_unread = 0 WHERE project_id = @project_id AND is_unread = 1;";
            command.Parameters.AddWithValue("@project_id", projectId);

            await command.ExecuteNonQueryAsync(cancellationToken);
            return Result<bool>.Ok(true);
        }
        catch (SqliteException ex)
        {
            return Result<bool>.Err(DatabaseErrors.CommandFailed(nameof(MarkAsReadAsync), ex));
        }
    }

    [ErrorOutcome(ErrorOutcome.Handled, Label = "Command failure surfaced as Result<bool>.Err")]
    public async Task<Result<bool>> MarkAllAsReadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = _connectionFactory.CreateConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE projects SET is_unread = 0 WHERE is_unread = 1;";

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
    public async Task<Result<int>> DeleteByProjectIdRangeAsync(long minProjectId, long maxProjectId, CancellationToken cancellationToken = default)
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
                    WHERE project_id IN (SELECT project_id FROM projects WHERE project_id BETWEEN @min_id AND @max_id);
                    """;
                deleteAssetsCommand.Parameters.AddWithValue("@min_id", minProjectId);
                deleteAssetsCommand.Parameters.AddWithValue("@max_id", maxProjectId);
                await deleteAssetsCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            using (var deleteSkillsCommand = connection.CreateCommand())
            {
                deleteSkillsCommand.Transaction = transaction;
                deleteSkillsCommand.CommandText = """
                    DELETE FROM project_skills
                    WHERE project_id IN (SELECT project_id FROM projects WHERE project_id BETWEEN @min_id AND @max_id);
                    """;
                deleteSkillsCommand.Parameters.AddWithValue("@min_id", minProjectId);
                deleteSkillsCommand.Parameters.AddWithValue("@max_id", maxProjectId);
                await deleteSkillsCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            using (var deleteFtsCommand = connection.CreateCommand())
            {
                deleteFtsCommand.Transaction = transaction;
                deleteFtsCommand.CommandText = "DELETE FROM projects_fts WHERE project_id BETWEEN @min_id AND @max_id;";
                deleteFtsCommand.Parameters.AddWithValue("@min_id", minProjectId);
                deleteFtsCommand.Parameters.AddWithValue("@max_id", maxProjectId);
                await deleteFtsCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            using (var deleteProjectsCommand = connection.CreateCommand())
            {
                deleteProjectsCommand.Transaction = transaction;
                deleteProjectsCommand.CommandText = "DELETE FROM projects WHERE project_id BETWEEN @min_id AND @max_id;";
                deleteProjectsCommand.Parameters.AddWithValue("@min_id", minProjectId);
                deleteProjectsCommand.Parameters.AddWithValue("@max_id", maxProjectId);
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

    [ErrorOutcome(ErrorOutcome.Handled, Label = "Command failure surfaced as Result<bool>.Err")]
    public async Task<Result<bool>> UpdatePublishedTimeAsync(long projectId, int number, string text, CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = _connectionFactory.CreateConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE projects SET publish_time_number = @number, publish_time_text = @text WHERE project_id = @project_id;";
            command.Parameters.AddWithValue("@number", number);
            command.Parameters.AddWithValue("@text", text);
            command.Parameters.AddWithValue("@project_id", projectId);

            await command.ExecuteNonQueryAsync(cancellationToken);
            return Result<bool>.Ok(true);
        }
        catch (SqliteException ex)
        {
            return Result<bool>.Err(DatabaseErrors.CommandFailed(nameof(UpdatePublishedTimeAsync), ex));
        }
    }

    [ErrorOutcome(ErrorOutcome.Handled, Label = "Query failure surfaced as Result.Err")]
    public async Task<Result<IReadOnlyList<(long ProjectId, DateTimeOffset DiscoveredAt)>>> GetAllProjectTimestampsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = _connectionFactory.CreateConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT project_id, discovered_at FROM projects;";

            var results = new List<(long, DateTimeOffset)>();
            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                results.Add((reader.GetInt64(0), DateTimeOffset.Parse(reader.GetString(1))));
            }

            return Result<IReadOnlyList<(long, DateTimeOffset)>>.Ok(results);
        }
        catch (SqliteException ex)
        {
            return Result<IReadOnlyList<(long, DateTimeOffset)>>.Err(DatabaseErrors.QueryFailed(nameof(GetAllProjectTimestampsAsync), ex));
        }
    }

    [ErrorOutcome(ErrorOutcome.Handled, Label = "Query failure surfaced as Result.Err")]
    public async Task<Result<IReadOnlyList<ProjectDetails>>> GetAllDetailsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = _connectionFactory.CreateConnection();
            var results = new List<ProjectDetails>();

            using (var command = connection.CreateCommand())
            {
                command.CommandText = """
                    SELECT p.project_id, p.title, p.url, p.description, p.budget, p.delivery_days,
                           p.enrichment_status, p.enriched_at, p.project_status,
                           p.publish_time_number, p.publish_time_text,
                           p.proposal_count, p.proposal_count_text,
                           o.owner_id, o.name, o.profile_url, o.avatar_url, o.rating,
                           o.completed_projects_count, o.hiring_rate_percent,
                           o.registered_at, o.open_projects_count, o.in_progress_projects_count,
                           o.ongoing_communications_count
                    FROM projects p
                    LEFT JOIN owners o ON o.owner_id = p.owner_id;
                    """;

                using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    results.Add(new ProjectDetails
                    {
                        ProjectId = reader.GetInt64(0),
                        Title = reader.GetString(1),
                        Url = reader.GetString(2),
                        Description = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                        Budget = reader.IsDBNull(4) ? null : reader.GetString(4),
                        DeliveryDays = reader.IsDBNull(5) ? null : reader.GetInt32(5),
                        EnrichmentStatus = Enum.Parse<EnrichmentStatus>(reader.GetString(6)),
                        EnrichedAt = reader.IsDBNull(7) ? null : DateTimeOffset.Parse(reader.GetString(7)),
                        ProjectStatus = reader.IsDBNull(8) ? null : reader.GetString(8),
                        PublishTimeNumber = reader.IsDBNull(9) ? 0 : reader.GetInt32(9),
                        PublishTimeText = reader.IsDBNull(10) ? string.Empty : reader.GetString(10),
                        ProposalCount = reader.IsDBNull(11) ? 0 : reader.GetInt32(11),
                        ProposalCountText = reader.IsDBNull(12) ? string.Empty : reader.GetString(12),
                        Owner = reader.IsDBNull(13)
                            ? new Owner()
                            : new Owner
                            {
                                OwnerId = reader.GetInt64(13),
                                Name = reader.IsDBNull(14) ? string.Empty : reader.GetString(14),
                                ProfileUrl = reader.IsDBNull(15) ? null : reader.GetString(15),
                                AvatarUrl = reader.IsDBNull(16) ? null : reader.GetString(16),
                                Rating = reader.IsDBNull(17) ? null : reader.GetDouble(17),
                                CompletedProjectsCount = reader.IsDBNull(18) ? null : reader.GetInt32(18),
                                HiringRatePercent = reader.IsDBNull(19) ? null : reader.GetInt32(19),
                                RegisteredAt = reader.IsDBNull(20) ? null : reader.GetString(20),
                                OpenProjectsCount = reader.IsDBNull(21) ? null : reader.GetInt32(21),
                                InProgressProjectsCount = reader.IsDBNull(22) ? null : reader.GetInt32(22),
                                OngoingCommunicationsCount = reader.IsDBNull(23) ? null : reader.GetInt32(23),
                            },
                    });
                }
            }

            // Fetch skills and attachments for all projects
            foreach (var details in results)
            {
                using (var skillsCommand = connection.CreateCommand())
                {
                    skillsCommand.CommandText = "SELECT name, url FROM project_skills WHERE project_id = @project_id;";
                    skillsCommand.Parameters.AddWithValue("@project_id", details.ProjectId);
                    using var reader = await skillsCommand.ExecuteReaderAsync(cancellationToken);
                    while (await reader.ReadAsync(cancellationToken))
                    {
                        details.Skills.Add(new ProjectSkill
                        {
                            Name = reader.GetString(0),
                            Url = reader.IsDBNull(1) ? null : reader.GetString(1),
                        });
                    }
                }

                using (var assetsCommand = connection.CreateCommand())
                {
                    assetsCommand.CommandText = """
                        SELECT asset_id, project_id, file_name, url, raw_url, local_path, size_bytes,
                               extension, requires_auth, size_text
                        FROM assets
                        WHERE project_id = @project_id;
                        """;
                    assetsCommand.Parameters.AddWithValue("@project_id", details.ProjectId);
                    using var reader = await assetsCommand.ExecuteReaderAsync(cancellationToken);
                    while (await reader.ReadAsync(cancellationToken))
                    {
                        details.Attachments.Add(ReadAsset(reader));
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

    private static ProjectSummary ReadSummary(SqliteDataReader reader) => new()
    {
        ProjectId = reader.GetInt64(0),
        Title = reader.GetString(1),
        Url = reader.GetString(2),
        ClientName = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
        PublishTimeNumber = reader.IsDBNull(4) ? 0 : reader.GetInt32(4),
        PublishTimeText = reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
        ProposalCount = reader.IsDBNull(6) ? 0 : reader.GetInt32(6),
        ProposalCountText = reader.IsDBNull(7) ? string.Empty : reader.GetString(7),
        Description = reader.IsDBNull(8) ? string.Empty : reader.GetString(8),
        Budget = reader.IsDBNull(9) ? null : reader.GetString(9),
        DeliveryDays = reader.IsDBNull(10) ? null : reader.GetInt32(10),
        IsUnread = !reader.IsDBNull(11) && reader.GetInt64(11) != 0,
        EnrichmentStatus = Enum.Parse<EnrichmentStatus>(reader.GetString(12)),
        DiscoveredAt = DateTimeOffset.Parse(reader.GetString(13)),
        ProjectStatus = reader.IsDBNull(14) ? null : reader.GetString(14),
        SkillsText = reader.IsDBNull(15) ? string.Empty : reader.GetString(15),
    };

    private static Asset ReadAsset(SqliteDataReader reader) => new()
    {
        AssetId = reader.GetInt64(0),
        ProjectId = reader.GetInt64(1),
        FileName = reader.GetString(2),
        Url = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
        RawUrl = reader.IsDBNull(4) ? null : reader.GetString(4),
        LocalPath = reader.IsDBNull(5) ? null : reader.GetString(5),
        SizeBytes = reader.IsDBNull(6) ? null : reader.GetInt64(6),
        Extension = reader.IsDBNull(7) ? null : reader.GetString(7),
        RequiresAuth = !reader.IsDBNull(8) && reader.GetInt64(8) != 0,
        SizeText = reader.IsDBNull(9) ? null : reader.GetString(9),
    };
}
