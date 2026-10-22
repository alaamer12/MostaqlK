using MostaqlK.Core;
using MostaqlK.Core.Sources;
using MostaqlK.Models;

namespace MostaqlK.Services.Pipeline.DiffEngine;

/// <summary>
/// Compares a freshly polled listing page against known committed and in-flight state
/// to determine which projects are genuinely new and should be enqueued for enrichment.
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
        IReadOnlySet<SourceProjectId> committedIds;
        IReadOnlySet<SourceProjectId> inFlightIds;

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
            var key = new SourceProjectId(project.Source, project.ProjectId);
            if (committedIds.Contains(key) || inFlightIds.Contains(key))
            {
                result.AlreadyKnownProjectIds.Add(key);
            }
            else
            {
                result.NewProjectIds.Add(key);
            }
        }

        return Result<DiffResult>.Ok(result);
    }
}
