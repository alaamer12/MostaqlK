using MostaqlK.Core;
using MostaqlK.Models;

namespace MostaqlK.Services.Pipeline.DiffEngine;

/// <summary>
/// Compares a freshly polled listing page against known committed and in-flight state
/// to determine which project IDs are genuinely new and should be enqueued for enrichment.
/// </summary>
public sealed class DiffEngine
{
    private readonly SqliteCommittedProvider _committedProvider;
    private readonly InFlightSetProvider _inFlightProvider;

    public DiffEngine(SqliteCommittedProvider committedProvider, InFlightSetProvider inFlightProvider)
    {
        _committedProvider = committedProvider;
        _inFlightProvider = inFlightProvider;
    }

    public async Task<Result<DiffResult>> DiffAsync(IReadOnlyList<ProjectSummary> polledProjects, CancellationToken cancellationToken = default)
    {
        // TODO: union `_committedProvider` and `_inFlightProvider` known IDs, then partition `polledProjects`.
        throw new NotImplementedException();
    }
}
