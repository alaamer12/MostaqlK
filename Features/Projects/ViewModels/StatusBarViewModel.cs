using CommunityToolkit.Mvvm.ComponentModel;
using MostaqlK.Services.Pipeline;

namespace MostaqlK.Features.Projects.ViewModels;

/// <summary>
/// View-model for the status bar area shown alongside the project feed: last poll time,
/// pipeline activity indicator, and a live rate-budget indicator sourced from
/// <see cref="TokenBucketRateLimiter"/> via a simple periodic poll of the available-token count
/// (kept intentionally minimal for V1 — no background timer/event plumbing added to the
/// rate limiter itself).
/// </summary>
public sealed partial class StatusBarViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    private readonly TokenBucketRateLimiter _rateLimiter;
    private readonly IPollService _pollService;
    private readonly Timer _rateBudgetTimer;

    [ObservableProperty]
    public partial DateTimeOffset? LastPolledAt { get; set; }

    [ObservableProperty]
    public partial bool IsPolling { get; set; }

    [ObservableProperty]
    public partial int UnreadCount { get; set; }

    [ObservableProperty]
    public partial double AvailableTokens { get; set; }

    [ObservableProperty]
    public partial int RateLimitCapacity { get; set; }

    public StatusBarViewModel(TokenBucketRateLimiter rateLimiter, IPollService pollService)
    {
        _rateLimiter = rateLimiter;
        _pollService = pollService;
        RateLimitCapacity = rateLimiter.Capacity;
        AvailableTokens = rateLimiter.AvailableTokens;

        _rateBudgetTimer = new Timer(_ => AvailableTokens = _rateLimiter.AvailableTokens, null, PollInterval, PollInterval);
    }

    public void Dispose() => _rateBudgetTimer.Dispose();
}
