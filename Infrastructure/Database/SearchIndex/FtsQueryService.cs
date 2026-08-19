using Microsoft.Data.Sqlite;
using MostaqlK.Core;
using MostaqlK.Models;

namespace MostaqlK.Infrastructure.Database.SearchIndex;

/// <summary>
/// Queries the SQLite FTS5 virtual table (see <c>FtsSchema.sql</c>) for bilingual
/// (Arabic/English) full-text project search, used by the advanced search feature.
/// </summary>
public sealed class FtsQueryService
{
    private readonly SqliteConnectionFactory _connectionFactory;

    public FtsQueryService(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    [ErrorOutcome(ErrorOutcome.Handled, Label = "Query failure surfaced as Result.Err")]
    public async Task<Result<IReadOnlyList<ProjectSummary>>> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Result<IReadOnlyList<ProjectSummary>>.Ok(new List<ProjectSummary>());
        }

        try
        {
            // Professional search: support prefix matching for each term.
            // Transforms "تصمي" to "تصمي*" and "تصميم موقع" to "تصميم* موقع*".
            var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                             .Select(t => t.Trim().Replace("\"", "\"\""))
                             .Where(t => !string.IsNullOrEmpty(t))
                             .Select(t => $"\"{t}\"*");
            var enhancedQuery = string.Join(" ", terms);

            using var connection = _connectionFactory.CreateConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT p.project_id, p.title, p.url, p.client_name,
                       p.publish_time_number, p.publish_time_text,
                       p.proposal_count, p.proposal_count_text,
                       p.is_unread, p.enrichment_status, p.discovered_at, p.description, p.budget, p.delivery_days,
                       p.project_status,
                       COALESCE((SELECT group_concat(name, ', ') FROM project_skills s WHERE s.project_id = p.project_id), '')
                FROM projects_fts f
                JOIN projects p ON p.project_id = f.project_id
                WHERE f.projects_fts MATCH @query
                ORDER BY rank;
                """;
            command.Parameters.AddWithValue("@query", enhancedQuery);

            var results = new List<ProjectSummary>();
            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                results.Add(new ProjectSummary
                {
                    ProjectId = reader.GetInt64(0),
                    Title = reader.GetString(1),
                    Url = reader.GetString(2),
                    ClientName = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                    PublishTimeNumber = reader.IsDBNull(4) ? 0 : reader.GetInt32(4),
                    PublishTimeText = reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
                    ProposalCount = reader.IsDBNull(6) ? 0 : reader.GetInt32(6),
                    ProposalCountText = reader.IsDBNull(7) ? string.Empty : reader.GetString(7),
                    IsUnread = !reader.IsDBNull(8) && reader.GetInt64(8) != 0,
                    EnrichmentStatus = Enum.Parse<EnrichmentStatus>(reader.GetString(9)),
                    DiscoveredAt = DateTimeOffset.Parse(reader.GetString(10)),
                    Description = reader.IsDBNull(11) ? string.Empty : reader.GetString(11),
                    Budget = reader.IsDBNull(12) ? null : reader.GetString(12),
                    DeliveryDays = reader.IsDBNull(13) ? null : reader.GetInt32(13),
                    ProjectStatus = reader.IsDBNull(14) ? null : reader.GetString(14),
                    SkillsText = reader.IsDBNull(15) ? string.Empty : reader.GetString(15),
                });
            }

            return Result<IReadOnlyList<ProjectSummary>>.Ok(results);
        }
        catch (SqliteException ex)
        {
            return Result<IReadOnlyList<ProjectSummary>>.Err(DatabaseErrors.QueryFailed(nameof(SearchAsync), ex));
        }
    }
}
