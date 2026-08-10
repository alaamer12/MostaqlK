using Microsoft.Data.Sqlite;
using MostaqlK.Core;
using MostaqlK.Models;

namespace MostaqlK.Infrastructure.Database;

/// <inheritdoc cref="IProjectRepository"/>
public sealed class ProjectRepository : IProjectRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;

    public ProjectRepository(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<Result<bool>> InsertSummaryAsync(ProjectSummary project, CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = _connectionFactory.CreateConnection();
            using var command = connection.CreateCommand();
            // Write-once: a project row is never overwritten once it exists (no-update policy).
            command.CommandText = """
                INSERT OR IGNORE INTO projects
                    (project_id, title, url, client_name, posted_relative, proposal_count,
                     is_unread, enrichment_status, discovered_at)
                VALUES
                    (@project_id, @title, @url, @client_name, @posted_relative, @proposal_count,
                     @is_unread, @enrichment_status, @discovered_at);
                """;
            command.Parameters.AddWithValue("@project_id", project.ProjectId);
            command.Parameters.AddWithValue("@title", project.Title);
            command.Parameters.AddWithValue("@url", project.Url);
            command.Parameters.AddWithValue("@client_name", project.ClientName);
            command.Parameters.AddWithValue("@posted_relative", project.PostedRelative);
            command.Parameters.AddWithValue("@proposal_count", project.ProposalCount);
            command.Parameters.AddWithValue("@is_unread", project.IsUnread ? 1 : 0);
            command.Parameters.AddWithValue("@enrichment_status", project.EnrichmentStatus.ToString());
            command.Parameters.AddWithValue("@discovered_at", project.DiscoveredAt.ToString("O"));

            var rowsAffected = await command.ExecuteNonQueryAsync(cancellationToken);
            return Result<bool>.Ok(rowsAffected > 0);
        }
        catch (SqliteException ex)
        {
            return Result<bool>.Err(DatabaseErrors.QueryFailed(nameof(InsertSummaryAsync), ex));
        }
    }

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
                         owner_id, enrichment_status, discovered_at, enriched_at)
                    VALUES
                        (@project_id, @title, @url, @client_name, @description, @budget, @delivery_days,
                         @owner_id, @enrichment_status, @discovered_at, @enriched_at)
                    ON CONFLICT(project_id) DO UPDATE SET
                        title = excluded.title,
                        url = excluded.url,
                        client_name = excluded.client_name,
                        description = excluded.description,
                        budget = excluded.budget,
                        delivery_days = excluded.delivery_days,
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
            return Result<bool>.Err(DatabaseErrors.QueryFailed(nameof(UpsertDetailsAsync), ex));
        }
    }

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

    public async Task<Result<IReadOnlyList<ProjectSummary>>> GetRecentAsync(int limit, CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = _connectionFactory.CreateConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT p.project_id, p.title, p.url, p.client_name, p.posted_relative, p.proposal_count,
                       p.description, p.budget, p.delivery_days,
                       p.is_unread, p.enrichment_status, p.discovered_at,
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

    public async Task<Result<long?>> GetNewestProjectIdAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = _connectionFactory.CreateConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT project_id FROM projects ORDER BY discovered_at DESC LIMIT 1;";
            var value = await command.ExecuteScalarAsync(cancellationToken);
            return Result<long?>.Ok(value is null || value is DBNull ? null : Convert.ToInt64(value));
        }
        catch (SqliteException ex)
        {
            return Result<long?>.Err(DatabaseErrors.QueryFailed(nameof(GetNewestProjectIdAsync), ex));
        }
    }

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
                           p.enrichment_status, p.enriched_at,
                           o.owner_id, o.name, o.profile_url, o.avatar_url, o.rating,
                           o.completed_projects_count, o.hiring_rate_percent
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
                        Owner = reader.IsDBNull(8)
                            ? new Owner()
                            : new Owner
                            {
                                OwnerId = reader.GetInt64(8),
                                Name = reader.IsDBNull(9) ? string.Empty : reader.GetString(9),
                                ProfileUrl = reader.IsDBNull(10) ? null : reader.GetString(10),
                                AvatarUrl = reader.IsDBNull(11) ? null : reader.GetString(11),
                                Rating = reader.IsDBNull(12) ? null : reader.GetDouble(12),
                                CompletedProjectsCount = reader.IsDBNull(13) ? null : reader.GetInt32(13),
                                HiringRatePercent = reader.IsDBNull(14) ? null : reader.GetInt32(14),
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

    private static ProjectSummary ReadSummary(SqliteDataReader reader) => new()
    {
        ProjectId = reader.GetInt64(0),
        Title = reader.GetString(1),
        Url = reader.GetString(2),
        ClientName = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
        PostedRelative = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
        ProposalCount = reader.IsDBNull(5) ? 0 : reader.GetInt32(5),
        Description = reader.IsDBNull(6) ? string.Empty : reader.GetString(6),
        Budget = reader.IsDBNull(7) ? null : reader.GetString(7),
        DeliveryDays = reader.IsDBNull(8) ? null : reader.GetInt32(8),
        IsUnread = !reader.IsDBNull(9) && reader.GetInt64(9) != 0,
        EnrichmentStatus = Enum.Parse<EnrichmentStatus>(reader.GetString(10)),
        DiscoveredAt = DateTimeOffset.Parse(reader.GetString(11)),
        SkillsText = reader.IsDBNull(12) ? string.Empty : reader.GetString(12),
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
