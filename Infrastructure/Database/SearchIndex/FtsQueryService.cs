using Dapper;
using Microsoft.Data.Sqlite;
using MostaqlK.Core;
using MostaqlK.Core.Sources;
using MostaqlK.Models;

namespace MostaqlK.Infrastructure.Database.SearchIndex;

/// <summary>
/// Queries the SQLite FTS5 virtual table (see <c>FtsSchema.sql</c>) for bilingual
/// (Arabic/English) full-text project search, used by the advanced search feature.
/// Search is scoped to one source: rows are returned with composite identity resolved
/// exactly like the other repository reads (band-subtracted site ids + <c>Source</c>).
/// </summary>
public sealed class FtsQueryService
{
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly SourceRegistry _registry;

    public FtsQueryService(SqliteConnectionFactory connectionFactory, SourceRegistry registry)
    {
        _connectionFactory = connectionFactory;
        _registry = registry;
    }

    [ErrorOutcome(ErrorOutcome.Handled, Label = "Query failure surfaced as Result.Err")]
    public async Task<Result<IReadOnlyList<ProjectSummary>>> SearchAsync(SourceId source, string query, CancellationToken cancellationToken = default)
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
            const string sql = """
                SELECT p.project_id, p.source_id, p.title, p.url, p.client_name,
                       p.publish_time_number, p.publish_time_text,
                       p.proposal_count, p.proposal_count_text,
                       p.is_unread, p.enrichment_status, p.discovered_at, p.description, p.budget, p.delivery_days,
                       p.project_status,
                       COALESCE((SELECT group_concat(name, ', ') FROM project_skills s WHERE s.project_id = p.project_id), '') AS skills_text
                FROM projects_fts f
                JOIN projects p ON p.project_id = f.project_id
                WHERE f.projects_fts MATCH @query AND p.source_id = @source_id
                ORDER BY rank;
                """;

            var results = new List<ProjectSummary>();
            var rows = await connection.QueryAsync<RowMaps.FtsProjectSummaryRow>(
                new CommandDefinition(sql, new { query = enhancedQuery, source_id = source.Value }, cancellationToken: cancellationToken));
            foreach (var row in rows)
            {
                results.Add(row.ToFtsSummary(_registry));
            }

            return Result<IReadOnlyList<ProjectSummary>>.Ok(results);
        }
        catch (SqliteException ex)
        {
            return Result<IReadOnlyList<ProjectSummary>>.Err(DatabaseErrors.QueryFailed(nameof(SearchAsync), ex));
        }
    }
}
