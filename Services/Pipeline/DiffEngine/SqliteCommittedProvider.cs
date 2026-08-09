using MostaqlK.Infrastructure.Database;

namespace MostaqlK.Services.Pipeline.DiffEngine;

/// <summary>
/// Known-state provider backed by the persisted SQLite project table — the permanent
/// backstop guaranteeing a project is never re-enriched once committed.
/// </summary>
public sealed class SqliteCommittedProvider : IKnownStateProvider
{
    private readonly IProjectRepository _projectRepository;

    public SqliteCommittedProvider(IProjectRepository projectRepository)
    {
        _projectRepository = projectRepository;
    }

    public Task<IReadOnlySet<long>> GetKnownProjectIdsAsync(CancellationToken cancellationToken = default)
    {
        // TODO: query `_projectRepository` for all committed project IDs.
        throw new NotImplementedException();
    }
}
