using MostaqlK.Core;
using MostaqlK.Core.Formatting;
using MostaqlK.Infrastructure.Database;
using MostaqlK.Models;
using MostaqlK.Services;
using MostaqlK.Services.Diagnostics;

namespace MostaqlK.Services.Pipeline.WorkerPool;

/// <summary>
/// A single long-lived loop that pulls project IDs off the <see cref="DiscoveryQueue"/>
/// and enriches them via <see cref="IEnrichmentService"/>, one at a time.
/// </summary>
public sealed class EnrichmentWorker
{
    // Per system-components.md #6: exponential backoff 1m/2m/4m/8m, capped at 15m, max 5 attempts.
    private static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(2),
        TimeSpan.FromMinutes(4),
        TimeSpan.FromMinutes(8),
        TimeSpan.FromMinutes(15),
    ];

    private readonly int _workerId;
    private readonly DiscoveryQueue _discoveryQueue;
    private readonly IEnrichmentService _enrichmentService;
    private readonly InFlightTracker _inFlightTracker;
    private readonly IProjectRepository _projectRepository;
    private readonly IOwnerRepository _ownerRepository;
    private readonly GlobalAppStatusService _globalStatus;
    private readonly INotificationDispatcher _notificationDispatcher;

    public EnrichmentWorker(
        int workerId,
        DiscoveryQueue discoveryQueue,
        IEnrichmentService enrichmentService,
        InFlightTracker inFlightTracker,
        IProjectRepository projectRepository,
        IOwnerRepository ownerRepository,
        GlobalAppStatusService globalStatus,
        INotificationDispatcher notificationDispatcher)
    {
        _workerId = workerId;
        _discoveryQueue = discoveryQueue;
        _enrichmentService = enrichmentService;
        _inFlightTracker = inFlightTracker;
        _projectRepository = projectRepository;
        _ownerRepository = ownerRepository;
        _globalStatus = globalStatus;
        _notificationDispatcher = notificationDispatcher;
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await foreach (var projectId in _discoveryQueue.ReadAllAsync(cancellationToken))
            {
                try
                {
                    // Radar: the queued project token travels to this worker's segment, then the
                    // segment activates - the user can see *which* worker picked the project up.
                    _globalStatus.NotifyProjectAssignedToWorker(_workerId, projectId);
                    _globalStatus.UpdateWorkerState(_workerId, WorkerState.Processing);
                    _globalStatus.UpdateQueueCount(_discoveryQueue.Count);
                    await ProcessAsync(projectId, cancellationToken);
                    // Success: remove from persistent backlog.
                    await _projectRepository.RemoveFromBacklogAsync(projectId, cancellationToken);
                    _globalStatus.UpdateWorkerState(_workerId, WorkerState.Completed);
                }
                catch (OperationCanceledException)
                {
                    // App shutdown: leave the loop without marking the worker as failed.
                    throw;
                }
                catch (Exception ex)
                {
                    // A single project's failure must not cost the app a worker.
                    _globalStatus.UpdateWorkerState(_workerId, WorkerState.Error);
                    InteractionLogger.Failure(
                        "EnrichmentWorker.Unexpected",
                        EnrichErrors.Unexpected(projectId, ex),
                        new { WorkerId = _workerId, ProjectId = projectId });
                    CrashReporter.Report("EnrichmentWorker.Unexpected", ex, new { WorkerId = _workerId, ProjectId = projectId });
                }
                finally
                {
                    // Give a moment for the Completed/Error state to be visible before returning to Idle
                    _ = Task.Delay(2000).ContinueWith(_ =>
                    {
                        try
                        {
                            _globalStatus.UpdateWorkerState(_workerId, WorkerState.Idle);
                        }
                        catch { }
                    }, TaskScheduler.Default);

                    _globalStatus.UpdateQueueCount(_discoveryQueue.Count);
                    // Hard rule per In-Flight Tracker spec: always released, success or failure,
                    // so no ID can get stuck permanently in-flight.
                    _inFlightTracker.MarkComplete(projectId);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on cancellation
        }
        catch (Exception ex)
        {
            CrashReporter.Report("EnrichmentWorker.RunAsync.Fatal", ex, new { WorkerId = _workerId });
        }
    }

    [ErrorOutcome(ErrorOutcome.Ignored, Label = "Expected NotImplementedException integration gaps (Step 4/5 not yet landed) tolerated silently")]
    private async Task ProcessAsync(long projectId, CancellationToken cancellationToken)
    {
        DomainError? lastError = null;
        ProjectDetails? details = null;

        for (var attempt = 1; attempt <= RetryDelays.Length; attempt++)
        {
            InteractionLogger.Mark("EnrichmentWorker.AttemptStart", "D", new { projectId, attempt });
            var result = await _enrichmentService.EnrichAsync(projectId, cancellationToken);
            if (result.IsOk)
            {
                details = result.Value;
                InteractionLogger.Mark("EnrichmentWorker.FetchSuccess", "D", new { projectId, title = details.Title });
                break;
            }

            lastError = result.Error;
            InteractionLogger.Failure(
                "EnrichmentWorker.Attempt",
                lastError,
                new { WorkerId = _workerId, ProjectId = projectId, Attempt = attempt });

            if (attempt < RetryDelays.Length)
            {
                await Task.Delay(RetryDelays[attempt - 1], cancellationToken);
            }
        }

        if (details is null)
        {
            // Permanent failure: max attempts exhausted. Per Step 4's future scope, this should also
            // mark `enrichment_status = 'failed'` in the DB. The error itself used to be built and
            // then assigned to a discard (`_ = ...`), so "gave up after 5 attempts" left no trace
            // anywhere at all - it is now logged and the worker segment is marked as failed.
            if (lastError is not null)
            {
                var exhausted = EnrichErrors.MaxAttemptsExhausted(projectId, RetryDelays.Length, lastError);
                InteractionLogger.Failure(
                    "EnrichmentWorker.MaxAttemptsExhausted",
                    exhausted,
                    new { WorkerId = _workerId, ProjectId = projectId });
                _globalStatus.UpdateWorkerState(_workerId, WorkerState.Error);
            }

            return;
        }

        // The detail page is the authoritative title, and for a project rehydrated from the
        // persisted backlog it is the *first* title this process ever sees - so the worker card
        // stops showing `#id` as soon as enrichment succeeds.
        _globalStatus.UpdateWorkerProjectTitle(_workerId, projectId, details.Title);

        try
        {
            if (details.Owner.Name.Length > 0 || details.Owner.OwnerId > 0)
            {
                await _ownerRepository.UpsertAsync(details.Owner, cancellationToken);
            }
            InteractionLogger.Mark("EnrichmentWorker.UpsertStart", "D", new { projectId });
            var upsertResult = await _projectRepository.UpsertDetailsAsync(details, cancellationToken);
            if (!upsertResult.IsOk)
            {
                InteractionLogger.Failure("EnrichmentWorker.UpsertFailed", upsertResult.Error, new { projectId });
            }
            else
            {
                InteractionLogger.Mark("EnrichmentWorker.UpsertSuccess", "D", new { projectId });
            }
        }
        catch (NotImplementedException)
        {
            // Expected integration gap: Step 4 (ProjectRepository/DB schema) is not
            // implemented yet. Tolerated here so the pipeline can be exercised end-to-end
            // ahead of that step landing.
        }
        catch (Exception ex)
        {
            CrashReporter.Report("EnrichmentWorker.UpsertException", ex, new { projectId });
        }

        try
        {
            await _notificationDispatcher.NotifyNewProjectsAsync(
                new List<Models.ProjectSummary> { ToSummary(details) },
                cancellationToken);
        }
        catch (NotImplementedException)
        {
            // Expected integration gap: Step 5 (NotificationDispatcher) is not implemented yet.
        }
        catch (Exception ex)
        {
            CrashReporter.Report("EnrichmentWorker.NotificationException", ex, new { projectId });
        }
    }

    private static Models.ProjectSummary ToSummary(ProjectDetails details)
    {
        return new()
        {
            ProjectId = details.ProjectId,
            Title = details.Title,
            Url = details.Url,
            Description = details.Description,
            ClientName = details.Owner?.Name ?? string.Empty,
            ProposalCount = details.ProposalCount,
            ProjectStatus = details.ProjectStatus,
            Budget = details.Budget,
            DeliveryDays = details.DeliveryDays,
            DiscoveredAt = details.DiscoveredAt != default ? details.DiscoveredAt : DateTimeOffset.UtcNow,
            // The moment enrichment actually completed (stamped by DetailParser), not this
            // notification's dispatch time - this is what the feed's ORDER BY sorts on, so a
            // project only moves to the top once its enrichment has genuinely finished.
            EnrichedAt = details.EnrichedAt,
        };
    }
}
