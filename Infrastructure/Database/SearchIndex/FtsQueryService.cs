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

    public async Task<Result<IReadOnlyList<ProjectSummary>>> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = _connectionFactory.CreateConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT p.project_id, p.title, p.url, p.client_name, p.posted_relative, p.proposal_count,
                       p.is_unread, p.enrichment_status, p.discovered_at
                FROM projects_fts f
                JOIN projects p ON p.project_id = f.project_id
                WHERE f.projects_fts MATCH @query
                ORDER BY rank;
                """;
            command.Parameters.AddWithValue("@query", query);

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
                    PostedRelative = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                    ProposalCount = reader.IsDBNull(5) ? 0 : reader.GetInt32(5),
                    IsUnread = !reader.IsDBNull(6) && reader.GetInt64(6) != 0,
                    EnrichmentStatus = Enum.Parse<EnrichmentStatus>(reader.GetString(7)),
                    DiscoveredAt = DateTimeOffset.Parse(reader.GetString(8)),
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
