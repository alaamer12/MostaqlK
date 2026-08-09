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
    private CancellationTokenSource? _loopCts;

    public PollService(IProjectScraper scraper, DiffEngine.DiffEngine diffEngine, DiscoveryQueue discoveryQueue)
    {
        _scraper = scraper;
        _diffEngine = diffEngine;
        _discoveryQueue = discoveryQueue;
    }

    public Task<Result<bool>> StartAsync(CancellationToken cancellationToken = default)
    {
        // TODO: start a background loop calling PollOnceAsync on the configured interval.
        _loopCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        throw new NotImplementedException();
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        _loopCts?.Cancel();
        return Task.CompletedTask;
    }

    public Task<Result<int>> PollOnceAsync(CancellationToken cancellationToken = default)
    {
        // TODO: fetch listing via `_scraper`, diff via `_diffEngine`, enqueue new IDs to `_discoveryQueue`.
        throw new NotImplementedException();
    }
}
