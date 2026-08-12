using MostaqlK.Core;
using MostaqlK.Infrastructure.Http;
using MostaqlK.Infrastructure.Database;
using MostaqlK.Services.Diagnostics;
using MostaqlK.Services.Pipeline.DiffEngine;

namespace MostaqlK.Services.Pipeline;

/// <inheritdoc cref="IPollService"/>
public sealed class PollService : IPollService
{
    private readonly IProjectScraper _scraper;
    private readonly MostaqlK.Core.Domain.BatchResult<long> _lastBatch = new();
    private readonly DiffEngine.DiffEngine _diffEngine;
    private readonly DiscoveryQueue _discoveryQueue;
    private readonly InFlightTracker _inFlightTracker;
    private readonly IProjectRepository _projectRepository;
    private readonly GlobalAppStatusService _globalStatus;
    private readonly TokenBucketRateLimiter _rateLimiter;
    private CancellationTokenSource? _loopCts;
    private Task? _loopTask;
    private readonly SemaphoreSlim _checkNowSignal = new(0);
    private volatile bool _isPaused;

    /// <summary>
    /// Poll interval in seconds. Settable at runtime (see <c>SettingsViewModel</c>); the loop
    /// re-reads this value on every tick instead of capturing it once at startup.
    /// </summary>
    public int PollIntervalSeconds { get; set; } = 30;

    /// <summary>Current observable status, mirrored to the tray icon (see <see cref="TrayIconService"/>).</summary>
    public PollServiceStatus Status { get; private set; } = PollServiceStatus.Idle;

    /// <summary>Raised whenever <see cref="Status"/> changes, so the tray icon can react live.</summary>
    public event Action<PollServiceStatus>? StatusChanged;

    /// <summary>Whether the periodic loop is currently paused (manually, via the tray icon).</summary>
    public bool IsPaused => _isPaused;

    public PollService(
        IProjectScraper scraper,
        DiffEngine.DiffEngine diffEngine,
        DiscoveryQueue discoveryQueue,
        InFlightTracker inFlightTracker,
        IProjectRepository projectRepository,
        GlobalAppStatusService globalStatus,
        TokenBucketRateLimiter rateLimiter)
    {
        _scraper = scraper;
        _diffEngine = diffEngine;
        _discoveryQueue = discoveryQueue;
        _inFlightTracker = inFlightTracker;
        _projectRepository = projectRepository;
        _globalStatus = globalStatus;
        _rateLimiter = rateLimiter;
    }

    public Task<Result<bool>> StartAsync(CancellationToken cancellationToken = default)
    {
        _loopCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _loopTask = RunLoopAsync(_loopCts.Token);
        return Task.FromResult(Result<bool>.Ok(true));
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        _loopCts?.Cancel();
        return _loopTask ?? Task.CompletedTask;
    }

    /// <summary>Toggles the paused flag (wired to the tray icon's "Pause / Resume" menu entry).</summary>
    public void SetPaused(bool paused) => _isPaused = paused;

    /// <summary>Forces an immediate poll cycle outside of the regular timer, without waiting for the next tick.</summary>
    public void RequestCheckNow()
    {
        if (_checkNowSignal.CurrentCount == 0)
        {
            _checkNowSignal.Release();
        }
    }

    [ErrorOutcome(ErrorOutcome.Ignored, Label = "Expected OperationCanceledException on StopAsync ends the loop silently")]
    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        // Run an immediate first poll rather than waiting a full interval on startup.
        ReportCycle(await PollOnceAsync(cancellationToken));

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                using var tickDelayCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                var delayTask = Task.Delay(TimeSpan.FromSeconds(Math.Max(1, PollIntervalSeconds)), tickDelayCts.Token);
                var checkNowTask = _checkNowSignal.WaitAsync(tickDelayCts.Token);

                var completed = await Task.WhenAny(delayTask, checkNowTask);
                tickDelayCts.Cancel();

                if (completed.IsCanceled)
                {
                    break;
                }

                if (!_isPaused)
                {
                    // The loop used to discard this Result entirely, which is the exact point where a
                    // permanent listing failure became invisible. Every cycle now reports its outcome.
                    ReportCycle(await PollOnceAsync(cancellationToken));
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on StopAsync - the loop simply ends.
        }
    }

    [ErrorOutcome(ErrorOutcome.Rethrown, Label = "Caller-initiated cancellation rethrown, not swallowed")]
    [ErrorOutcome(ErrorOutcome.Handled, Label = "Listing/diff failures and unexpected exceptions surfaced as Result<int>.Err")]
    public async Task<Result<int>> PollOnceAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            SetStatus(PollServiceStatus.Polling);
            // Radar discovery tier: the scanning segment runs for as long as this flag is set.
            _globalStatus.IsScanning = true;
            _globalStatus.LastScanAttemptedAt = DateTimeOffset.UtcNow;
            _globalStatus.ScanAttemptCount++;
            _globalStatus.ScanIntervalSeconds = PollIntervalSeconds;
            _globalStatus.DiscoveryProgress = 0.1; // Start pulse
            await _rateLimiter.WaitForTokenAsync(cancellationToken);

            _globalStatus.DiscoveryProgress = 0.3;
            var listingResult = await _scraper.FetchListingAsync(cancellationToken);

            // The temporary debug delay that used to sit here has been removed: it only ever
            // delayed the *listing* fetch, never the enrichment workers' detail fetches, which is
            // why a burst of ~20 requests could still go out inside ten seconds while this delay
            // was in place. Request spacing now belongs entirely to TokenBucketRateLimiter, the one
            // place every outbound request passes through.
            if (listingResult.IsError)
            {
                _globalStatus.DiscoveryProgress = 0;
                _globalStatus.IsScanning = false;
                Fail(listingResult.Error, "PollService.FetchListing");
                return Result<int>.Err(listingResult.Error);
            }

            _globalStatus.DiscoveryProgress = 0.6;
            _globalStatus.IsSnapshotActive = true; // Trigger Radar Sweep
            var diffResult = await _diffEngine.DiffAsync(listingResult.Value, cancellationToken);
            _globalStatus.IsSnapshotActive = false;

            if (diffResult.IsError)
            {
                _globalStatus.DiscoveryProgress = 0;
                _globalStatus.IsScanning = false;
                Fail(diffResult.Error, "PollService.Diff");
                return Result<int>.Err(diffResult.Error);
            }

            _globalStatus.DiscoveryProgress = 0.9;

            // The listing row already carries the project's title, and it is the *only* place a
            // title exists before enrichment (no project row is written at discovery time). Passing
            // it on is what lets the radar and the pipeline panel name a project instead of showing
            // a bare `#1267826`.
            var titles = new Dictionary<long, string>(listingResult.Value.Count);
            foreach (var summary in listingResult.Value)
            {
                titles[summary.ProjectId] = summary.Title ?? string.Empty;
            }

            var enqueued = 0;
            foreach (var projectId in diffResult.Value.NewProjectIds)
            {
                if (!_inFlightTracker.TryMarkInFlight(projectId))
                {
                    // Race with another poll cycle/worker - already claimed, skip.
                    continue;
                }

                // Add to persistent backlog before enqueuing in memory.
                await _projectRepository.AddToBacklogAsync(projectId, cancellationToken);

                await _discoveryQueue.EnqueueAsync(projectId, cancellationToken);
                // Radar: detection pulse -> token -> queue slot; the ring grows on arrival.
                _globalStatus.NotifyProjectDiscovered(
                    projectId,
                    titles.TryGetValue(projectId, out var title) ? title : string.Empty);
                _globalStatus.UpdateQueueCount(_discoveryQueue.Count);
                enqueued++;
            }

            SetStatus(enqueued > 0 ? PollServiceStatus.BacklogDraining : PollServiceStatus.Idle);
            _globalStatus.DiscoveryProgress = 1.0;
            // "Saw 41 projects, 0 of them new" is a completely different story from "the request
            // failed", and the UI could not tell them apart before.
            _globalStatus.NotifyScanSucceeded(listingResult.Value.Count, enqueued);
            _ = Task.Delay(1000).ContinueWith(_ =>
            {
                _globalStatus.DiscoveryProgress = 0;
                _globalStatus.IsScanning = false;
            });
            return Result<int>.Ok(enqueued);
        }
        catch (OperationCanceledException)
        {
            _globalStatus.IsScanning = false;
            throw;
        }
        catch (Exception ex)
        {
            _globalStatus.IsScanning = false;
            SetStatus(PollServiceStatus.Error);
            var error = PollErrors.ListingFetchFailed(ex);
            Fail(error, "PollService.Unexpected");
            return Result<int>.Err(error);
        }
    }

    /// <summary>Logs a failing cycle and publishes it to the UI. Never swallow a poll failure again.</summary>
    private void Fail(DomainError error, string checkpoint)
    {
        SetStatus(PollServiceStatus.Error);
        InteractionLogger.Failure(checkpoint, error, new { PollIntervalSeconds, _globalStatus.ScanAttemptCount });
        _globalStatus.NotifyScanFailed(error);
    }

    /// <summary>
    /// Last line of defence: whatever <see cref="PollOnceAsync"/> returns is accounted for, so a
    /// future failure path that forgets to call <see cref="Fail"/> still leaves a trace in the log.
    /// </summary>
    private static void ReportCycle(Result<int> cycle)
    {
        if (cycle.IsError)
        {
            InteractionLogger.Failure("PollService.Cycle", cycle.Error);
        }
    }

    private void SetStatus(PollServiceStatus status)
    {
        if (Status == status)
        {
            return;
        }

        Status = status;
        StatusChanged?.Invoke(status);
    }
}

/// <summary>
/// Coarse-grained pipeline health/activity signal, mirrored 1:1 onto <see cref="MostaqlK.UI.TrayIcon.TrayIconState"/>.
/// </summary>
public enum PollServiceStatus
{
    Idle,
    Polling,
    BacklogDraining,
    Error
}
