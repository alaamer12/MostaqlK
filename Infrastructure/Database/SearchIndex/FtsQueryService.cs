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

    public Task<Result<IReadOnlyList<ProjectSummary>>> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        // TODO: SELECT ... FROM projects_fts WHERE projects_fts MATCH @query.
        throw new NotImplementedException();
    }
}
