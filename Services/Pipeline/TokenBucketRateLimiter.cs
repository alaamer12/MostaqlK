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

    /// <summary>
    /// Bucket size. Settable at runtime (see <c>SettingsViewModel</c>) so the configured
    /// requests-per-minute can be applied to the running limiter without an app restart.
    /// </summary>
    public int Capacity { get; set; }

    /// <summary>
    /// Tokens added per second. Settable at runtime for the same live-reconfiguration reason
    /// as <see cref="Capacity"/>.
    /// </summary>
    public double RefillPerSecond { get; set; }

    /// <summary>
    /// Current token count, refilled lazily on read (same refill-on-acquire pattern as
    /// <see cref="WaitForTokenAsync"/>). Exposed purely so the UI (see
    /// <c>StatusBarViewModel</c>) can show a live rate-budget indicator without adding any
    /// background timer/event plumbing to the limiter itself.
    /// </summary>
    public double AvailableTokens
    {
        get
        {
            lock (_gate)
            {
                Refill();
                return _tokens;
            }
        }
    }

    public TokenBucketRateLimiter(int capacity, double refillPerSecond)
    {
        Capacity = capacity;
        RefillPerSecond = refillPerSecond;
        _tokens = capacity;
        _lastRefill = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Reconfigures the bucket for a new requests-per-minute budget, called live from
    /// <c>SettingsViewModel</c> when the user changes the rate setting. Clamps the current
    /// token count to the new capacity so a shrink takes effect immediately.
    /// </summary>
    public void Reconfigure(int capacity, double refillPerSecond)
    {
        lock (_gate)
        {
            Capacity = capacity;
            RefillPerSecond = refillPerSecond;
            _tokens = Math.Min(_tokens, capacity);
        }
    }

    /// <summary>
    /// Waits until a single token is available, then consumes it. Uses a lazy
    /// refill-on-acquire pattern: every call first tops up `_tokens` based on elapsed
    /// wall-clock time since the last refill (capped at `Capacity`), then either consumes
    /// a token immediately or computes how long to wait for the next fractional token and
    /// polls again after that delay. Simpler than a background timer and equally correct
    /// since every acquisition path always calls through here.
    /// </summary>
    public async Task WaitForTokenAsync(CancellationToken cancellationToken = default)
    {
        while (true)
        {
            TimeSpan waitTime;
            lock (_gate)
            {
                Refill();

                if (_tokens >= 1.0)
                {
                    _tokens -= 1.0;
                    return;
                }

                var missing = 1.0 - _tokens;
                waitTime = TimeSpan.FromSeconds(missing / RefillPerSecond);
            }

            if (waitTime < TimeSpan.FromMilliseconds(10))
            {
                waitTime = TimeSpan.FromMilliseconds(10);
            }

            await Task.Delay(waitTime, cancellationToken);
        }
    }

    /// <summary>Must be called while holding `_gate`.</summary>
    private void Refill()
    {
        var now = DateTimeOffset.UtcNow;
        var elapsedSeconds = (now - _lastRefill).TotalSeconds;
        if (elapsedSeconds <= 0)
        {
            return;
        }

        _tokens = Math.Min(Capacity, _tokens + (elapsedSeconds * RefillPerSecond));
        _lastRefill = now;
    }
}
