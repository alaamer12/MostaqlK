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

    [ErrorOutcome(ErrorOutcome.Rethrown, Label = "Caller-initiated cancellation rethrown, not swallowed")]
    [ErrorOutcome(ErrorOutcome.Handled, Label = "Provider failure wrapped as Result<DiffResult>.Err via DiffErrors.KnownStateUnavailable")]
    public async Task<Result<DiffResult>> DiffAsync(IReadOnlyList<ProjectSummary> polledProjects, CancellationToken cancellationToken = default)
    {
        IReadOnlySet<long> committedIds;
        IReadOnlySet<long> inFlightIds;

        try
        {
            committedIds = await _committedProvider.GetKnownProjectIdsAsync(cancellationToken);
            inFlightIds = await _inFlightProvider.GetKnownProjectIdsAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Result<DiffResult>.Err(DiffErrors.KnownStateUnavailable(ex));
        }

        var result = new DiffResult();
        foreach (var project in polledProjects)
        {
            if (committedIds.Contains(project.ProjectId) || inFlightIds.Contains(project.ProjectId))
            {
                result.AlreadyKnownProjectIds.Add(project.ProjectId);
            }
            else
            {
                result.NewProjectIds.Add(project.ProjectId);
            }
        }

        return Result<DiffResult>.Ok(result);
    }
}
