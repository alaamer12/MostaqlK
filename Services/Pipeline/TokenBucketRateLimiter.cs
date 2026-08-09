namespace MostaqlK.Services.Pipeline;

/// <summary>
/// Shared token-bucket rate limiter applied across all outbound HTTP requests made by
/// the poll service and the enrichment workers, so the app never exceeds the configured
/// requests-per-interval budget against Mostaql.
/// </summary>
public sealed class TokenBucketRateLimiter
{
    private readonly object _gate = new();
    private double _tokens;
    private DateTimeOffset _lastRefill;

    public int Capacity { get; }

    public double RefillPerSecond { get; }

    public TokenBucketRateLimiter(int capacity, double refillPerSecond)
    {
        Capacity = capacity;
        RefillPerSecond = refillPerSecond;
        _tokens = capacity;
        _lastRefill = DateTimeOffset.UtcNow;
    }

    /// <summary>Waits until a single token is available, then consumes it.</summary>
    public Task WaitForTokenAsync(CancellationToken cancellationToken = default)
    {
        // TODO: implement refill-and-wait logic using `_gate`, `_tokens`, `_lastRefill`.
        throw new NotImplementedException();
    }
}
