using MostaqlK.Core;
using MostaqlK.Infrastructure.Database;
using MostaqlK.Models;
using MostaqlK.Services;

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
    private readonly INotificationDispatcher _notificationDispatcher;

    public EnrichmentWorker(
        int workerId,
        DiscoveryQueue discoveryQueue,
        IEnrichmentService enrichmentService,
        InFlightTracker inFlightTracker,
        IProjectRepository projectRepository,
        INotificationDispatcher notificationDispatcher)
    {
        _workerId = workerId;
        _discoveryQueue = discoveryQueue;
        _enrichmentService = enrichmentService;
        _inFlightTracker = inFlightTracker;
        _projectRepository = projectRepository;
        _notificationDispatcher = notificationDispatcher;
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        await foreach (var projectId in _discoveryQueue.ReadAllAsync(cancellationToken))
        {
            try
            {
                await ProcessAsync(projectId, cancellationToken);
            }
            finally
            {
                // Hard rule per In-Flight Tracker spec: always released, success or failure,
                // so no ID can get stuck permanently in-flight.
                _inFlightTracker.MarkComplete(projectId);
            }
        }
    }

    private async Task ProcessAsync(long projectId, CancellationToken cancellationToken)
    {
        DomainError? lastError = null;
        ProjectDetails? details = null;

        for (var attempt = 1; attempt <= RetryDelays.Length; attempt++)
        {
            var result = await _enrichmentService.EnrichAsync(projectId, cancellationToken);
            if (result.IsOk)
            {
                details = result.Value;
                break;
            }

            lastError = result.Error;

            if (attempt < RetryDelays.Length)
            {
                await Task.Delay(RetryDelays[attempt - 1], cancellationToken);
            }
        }

        if (details is null)
        {
            // Permanent failure: max attempts exhausted. Per Step 4's future scope, this
            // should mark `enrichment_status = 'failed'` in the DB - for now we only log via
            // the domain error, since `ProjectRepository` writes are not implemented yet.
            _ = lastError is not null ? EnrichErrors.MaxAttemptsExhausted(projectId, RetryDelays.Length, lastError) : null;
            return;
        }

        try
        {
            await _projectRepository.UpsertDetailsAsync(details, cancellationToken);
        }
        catch (NotImplementedException)
        {
            // Expected integration gap: Step 4 (ProjectRepository/DB schema) is not
            // implemented yet. Tolerated here so the pipeline can be exercised end-to-end
            // ahead of that step landing.
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
    }

    private static Models.ProjectSummary ToSummary(ProjectDetails details) => new()
    {
        ProjectId = details.ProjectId,
        Title = details.Title,
        Url = details.Url,
        ClientName = details.Owner?.Name ?? string.Empty,
        DiscoveredAt = DateTimeOffset.UtcNow,
    };
}
