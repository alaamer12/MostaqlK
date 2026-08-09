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

    public async Task<IReadOnlySet<long>> GetKnownProjectIdsAsync(CancellationToken cancellationToken = default)
    {
        var result = await _projectRepository.GetAllKnownProjectIdsAsync(cancellationToken);
        if (result.IsError)
        {
            // Surfaced as an exception so `DiffEngine.DiffAsync` can wrap it via
            // `DiffErrors.KnownStateUnavailable` and fail the poll cycle gracefully
            // instead of crashing it - relevant until Step 4's schema/migrations exist.
            throw new InvalidOperationException(result.Error.InternalMessage, result.Error.Cause);
        }

        return result.Value;
    }
}
