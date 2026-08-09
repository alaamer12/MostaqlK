using MostaqlK.Core;
using MostaqlK.Infrastructure.Http;
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
        TokenBucketRateLimiter rateLimiter)
    {
        _scraper = scraper;
        _diffEngine = diffEngine;
        _discoveryQueue = discoveryQueue;
        _inFlightTracker = inFlightTracker;
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

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        // Run an immediate first poll rather than waiting a full interval on startup.
        await PollOnceAsync(cancellationToken);

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
                    await PollOnceAsync(cancellationToken);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on StopAsync - the loop simply ends.
        }
    }

    public async Task<Result<int>> PollOnceAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            SetStatus(PollServiceStatus.Polling);
            await _rateLimiter.WaitForTokenAsync(cancellationToken);

            var listingResult = await _scraper.FetchListingAsync(cancellationToken);
            if (listingResult.IsError)
            {
                return Result<int>.Err(listingResult.Error);
            }

            var diffResult = await _diffEngine.DiffAsync(listingResult.Value, cancellationToken);
            if (diffResult.IsError)
            {
                return Result<int>.Err(diffResult.Error);
            }

            var enqueued = 0;
            foreach (var projectId in diffResult.Value.NewProjectIds)
            {
                if (!_inFlightTracker.TryMarkInFlight(projectId))
                {
                    // Race with another poll cycle/worker - already claimed, skip.
                    continue;
                }

                await _discoveryQueue.EnqueueAsync(projectId, cancellationToken);
                enqueued++;
            }

            SetStatus(enqueued > 0 ? PollServiceStatus.BacklogDraining : PollServiceStatus.Idle);
            return Result<int>.Ok(enqueued);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            SetStatus(PollServiceStatus.Error);
            return Result<int>.Err(PollErrors.ListingFetchFailed(ex));
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
