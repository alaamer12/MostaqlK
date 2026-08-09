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

    public Task<Result<bool>> InsertSummaryAsync(ProjectSummary project, CancellationToken cancellationToken = default)
    {
        // TODO: INSERT INTO projects (...) using `_connectionFactory.CreateConnection()`.
        throw new NotImplementedException();
    }

    public Task<Result<bool>> UpsertDetailsAsync(ProjectDetails details, CancellationToken cancellationToken = default)
    {
        // TODO: UPDATE projects SET description/budget/... WHERE project_id = @details.ProjectId.
        throw new NotImplementedException();
    }

    public Task<Result<IReadOnlySet<long>>> GetAllKnownProjectIdsAsync(CancellationToken cancellationToken = default)
    {
        // TODO: SELECT project_id FROM projects.
        throw new NotImplementedException();
    }

    public Task<Result<IReadOnlyList<ProjectSummary>>> GetRecentAsync(int limit, CancellationToken cancellationToken = default)
    {
        // TODO: SELECT ... FROM projects ORDER BY discovered_at DESC LIMIT @limit.
        throw new NotImplementedException();
    }

    public Task<Result<ProjectDetails?>> GetDetailsAsync(long projectId, CancellationToken cancellationToken = default)
    {
        // TODO: SELECT ... FROM projects WHERE project_id = @projectId.
        throw new NotImplementedException();
    }
}
