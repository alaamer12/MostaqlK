# Worker pool & rate limiter

[← Back to wiki home](../../base/tech/README.md)

## Table of contents
- [Queue](#queue)
- [Worker pool](#worker-pool)
- [Shared rate limiter (token bucket)](#shared-rate-limiter-token-bucket)
- [Putting it together](#putting-it-together)
- [Backpressure](#backpressure)

## Queue

A single FIFO queue holds `project_id`s that the [diff engine](diff-engine.md) has marked `unseen` and the [in-flight tracker](concurrency-model.md) has accepted. In .NET, `System.Threading.Channels.Channel<long>` is the natural fit — it's an async-friendly, thread-safe producer/consumer queue built for exactly this pattern (poll loop produces, worker pool consumes), and avoids hand-rolling locking around a plain `Queue<T>`.

```csharp
var channel = Channel.CreateUnbounded<long>();

// Producer (poll loop)
await channel.Writer.WriteAsync(projectId);

// Consumer (worker)
await foreach (var id in channel.Reader.ReadAllAsync())
{
    await EnrichAsync(id);
}
```

FIFO ordering here is what gives the [backlog-handling fairness guarantee](../../base/product/architecture-pipeline.md#backlog-handling-no-special-cold-start) — older discoveries are always processed before newer ones, never starved by a later poll's fresh arrivals.

## Worker pool

A fixed number of consumer tasks read from the same channel concurrently — this is the `max_concurrent_detail_fetches` setting from [configuration-reference.md](../product/configuration-reference.md#polling--rate).

```csharp
var workerCount = config.MaxConcurrentDetailFetches; // default 2–3

var workers = Enumerable.Range(0, workerCount)
    .Select(_ => Task.Run(async () =>
    {
        await foreach (var id in channel.Reader.ReadAllAsync())
        {
            try
            {
                await rateLimiter.WaitForTokenAsync();
                await EnrichAndCommitAsync(id);
            }
            finally
            {
                inFlightTracker.MarkComplete(id);
            }
        }
    }));

await Task.WhenAll(workers);
```

Each worker independently waits on the shared rate limiter before making its HTTP request — this is what keeps concurrency capped *and* the aggregate request rate capped, since raising `workerCount` alone would only add concurrency, not bypass the shared budget.

## Shared rate limiter (token bucket)

One rate limiter instance is shared across **both** tiers — the poll loop and every worker draw from the same bucket, so `max_requests_per_minute` ([configuration-reference.md](../product/configuration-reference.md#polling--rate)) is a true aggregate ceiling, not a per-tier one.

```csharp
public sealed class TokenBucketRateLimiter
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly int _capacity;
    private double _tokens;
    private DateTime _lastRefill;
    private readonly double _refillPerSecond;

    public TokenBucketRateLimiter(int requestsPerMinute)
    {
        _capacity = requestsPerMinute;
        _tokens = requestsPerMinute;
        _refillPerSecond = requestsPerMinute / 60.0;
        _lastRefill = DateTime.UtcNow;
    }

    public async Task WaitForTokenAsync()
    {
        while (true)
        {
            await _gate.WaitAsync();
            try
            {
                Refill();
                if (_tokens >= 1)
                {
                    _tokens -= 1;
                    return;
                }
            }
            finally { _gate.Release(); }

            await Task.Delay(TimeSpan.FromMilliseconds(250));
        }
    }

    private void Refill()
    {
        var now = DateTime.UtcNow;
        var elapsed = (now - _lastRefill).TotalSeconds;
        _tokens = Math.Min(_capacity, _tokens + elapsed * _refillPerSecond);
        _lastRefill = now;
    }
}
```

This is a standard token bucket: tokens refill continuously at `requestsPerMinute / 60` per second, capped at `requestsPerMinute` total capacity (so a long idle period doesn't let the app burst its entire minute's budget in one second — capacity equals the per-minute rate, not an inflated allowance). Both the poll loop and every worker call `WaitForTokenAsync()` immediately before their HTTP request, nothing else.

## Putting it together

```
Poll loop:
  await rateLimiter.WaitForTokenAsync()
  listing = await FetchListingAsync()
  candidates = Parse(listing)
  { unseen } = await diffEngine.ResolveAsync(candidates, providers)
  foreach id in unseen:
    if inFlightTracker.TryMarkInFlight(id):
      await channel.Writer.WriteAsync(id)

Worker (× N):
  foreach id in channel.Reader:
    await rateLimiter.WaitForTokenAsync()
    try: enrich + commit (id)
    finally: inFlightTracker.MarkComplete(id)
```

The poll loop's own listing fetch also goes through `WaitForTokenAsync()` — it is not exempt from the budget just because it's Tier 1. A configured `poll_interval_seconds` that's already slower than the token refill rate means the poll loop rarely has to wait; it's the worker pool draining a backlog that actually experiences the throttling in practice.

## Backpressure

`Channel.CreateUnbounded` is deliberately unbounded rather than capacity-limited — the queue is allowed to grow arbitrarily during a large backlog ([architecture-pipeline.md § backlog handling](../../base/product/architecture-pipeline.md#backlog-handling-no-special-cold-start)) rather than blocking the poll loop or dropping discoveries. The rate limiter is what provides backpressure on *outbound requests*; the queue itself should never be the thing that causes a discovered project to be lost or a poll cycle to stall.
