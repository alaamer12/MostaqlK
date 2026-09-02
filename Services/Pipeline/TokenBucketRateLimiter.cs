namespace MostaqlK.Services.Pipeline;

/// <summary>
/// Shared token-bucket rate limiter applied across all outbound HTTP requests made by
/// the poll service and the enrichment workers, so the app never exceeds the configured
/// requests-per-interval budget against Mostaql.
/// </summary>
/// <remarks>
/// Per <c>v1/tech/worker-pool-and-rate-limiter.md</c> the bucket is defined by a single number,
/// <c>max_requests_per_minute</c>: capacity equals that per-minute budget and tokens refill at
/// <c>rpm / 60</c> per second, "so a long idle period doesn't let the app burst its entire
/// minute's budget in one second - capacity equals the per-minute rate, not an inflated
/// allowance". The limiter used to be constructed with an unrelated capacity/refill pair
/// (capacity 10, 1 token per second = 60/min with a 10-request burst), which is why a fresh
/// database produced roughly twenty detail fetches inside ten seconds instead of spacing them
/// out: the burst allowance and the refill rate were both far above the configured budget.
/// <para>
/// <see cref="MinimumSpacing"/> implements the second half of
/// <c>base/product/architecture-pipeline.md § rate limiting</c>: even inside the per-minute
/// budget, consecutive requests are spaced so a backlog burst never opens several simultaneous
/// connections.
/// </para>
/// </remarks>
public sealed class TokenBucketRateLimiter
{
    /// <summary>Default shared budget (<c>max_requests_per_minute</c>) per configuration-reference.md.</summary>
    public const int DefaultRequestsPerMinute = 2;

    /// <summary>
    /// How much faster than the strict per-minute pacing the bucket refills while <c>safe
    /// requests</c> is switched off. FIX (rate limit "hard-coded" bug): this used to be an
    /// absolute floor (<c>Capacity = Math.Max(rpm, 10)</c>, <c>RefillPerSecond =
    /// Math.Max(rpm / 60, 1.0)</c>) that completely ignored a configured budget below that floor
    /// - e.g. setting <c>max_requests_per_minute</c> to 5 with "safe requests" off still drained
    /// at a fixed ~60/min, making the configured number look like it had no effect at all. Fast
    /// mode is now always derived from the configured <c>rpm</c> (just refilling this many times
    /// faster, with no minimum spacing), so it can never diverge from what the user actually set.
    /// </summary>
    public const double FastModeRefillMultiplier = 10.0;

    /// <summary>Spacing enforced between two requests while <c>safe requests</c> is on.</summary>
    public static readonly TimeSpan SafeModeMinimumSpacing = TimeSpan.FromSeconds(1);

    private readonly object _gate = new();
    private double _tokens;
    private DateTimeOffset _lastRefill;
    private DateTimeOffset _lastGrant = DateTimeOffset.MinValue;

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

    /// <summary>
    /// Minimum spacing enforced between two granted tokens, so several workers releasing at once
    /// cannot fire simultaneous connections even while the bucket still holds tokens.
    /// </summary>
    public TimeSpan MinimumSpacing { get; set; } = SafeModeMinimumSpacing;

    /// <summary>
    /// Whether the documented safe pacing is in force (the <c>safe requests</c> setting). When
    /// <see langword="true"/> the bucket follows the spec exactly - capacity equals
    /// <c>max_requests_per_minute</c>, refill is <c>rpm / 60</c> per second and consecutive
    /// requests are spaced by <see cref="SafeModeMinimumSpacing"/>. When <see langword="false"/>
    /// the limiter reverts to the faster, deliberately looser burst behaviour (still capacity
    /// <c>rpm</c>, but refilling <see cref="FastModeRefillMultiplier"/> times quicker and with no
    /// spacing), which drains a large backlog far quicker at the cost of a much higher outbound
    /// request rate - while always staying tied to the configured budget.
    /// </summary>
    public bool SafeRequests { get; private set; } = true;

    /// <summary>
    /// Builds the bucket from the configured <c>max_requests_per_minute</c> budget and the
    /// <c>safe requests</c> switch.
    /// </summary>
    public TokenBucketRateLimiter(int requestsPerMinute = DefaultRequestsPerMinute, bool safeRequests = true)
    {
        Apply(requestsPerMinute, safeRequests, fillBucket: true);
        _lastRefill = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Reconfigures the bucket for a new requests-per-minute budget and/or <c>safe requests</c>
    /// state, called live from <c>SettingsViewModel</c> when the user changes either setting.
    /// Clamps the current token count to the new capacity so a shrink takes effect immediately.
    /// </summary>
    public void Reconfigure(int requestsPerMinute, bool safeRequests)
    {
        lock (_gate)
        {
            Apply(requestsPerMinute, safeRequests, fillBucket: false);
        }
    }

    /// <summary>Must be called while holding `_gate` (or from the constructor).</summary>
    private void Apply(int requestsPerMinute, bool safeRequests, bool fillBucket)
    {
        var rpm = Math.Max(1, requestsPerMinute);
        SafeRequests = safeRequests;

        if (safeRequests)
        {
            Capacity = rpm;
            RefillPerSecond = rpm / 60.0;
            MinimumSpacing = SafeModeMinimumSpacing;
        }
        else
        {
            Capacity = rpm;
            RefillPerSecond = rpm / 60.0 * FastModeRefillMultiplier;
            MinimumSpacing = TimeSpan.Zero;
        }

        _tokens = fillBucket ? Capacity : Math.Min(_tokens, Capacity);
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

                var now = DateTimeOffset.UtcNow;
                var sinceLastGrant = now - _lastGrant;

                if (sinceLastGrant < MinimumSpacing)
                {
                    waitTime = MinimumSpacing - sinceLastGrant;
                }
                else if (_tokens >= 1.0)
                {
                    _tokens -= 1.0;
                    _lastGrant = now;
                    return;
                }
                else
                {
                    var missing = 1.0 - _tokens;
                    var refillRate = Math.Max(0.001, RefillPerSecond);
                    var seconds = missing / refillRate;
                    if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds > 3600)
                    {
                        seconds = 3600;
                    }
                    waitTime = TimeSpan.FromSeconds(seconds);
                }
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
