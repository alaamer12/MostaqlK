using MostaqlK.Core;

namespace MostaqlK.Services.Pipeline;

/// <summary>
/// Runs the periodic listing poll: fetches the current project listing page(s), diffs
/// them against known state, and enqueues genuinely new project IDs for enrichment.
/// </summary>
public interface IPollService
{
    /// <summary>Starts the periodic polling loop on the configured interval.</summary>
    Task<Result<bool>> StartAsync(CancellationToken cancellationToken = default);

    /// <summary>Stops the periodic polling loop.</summary>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>Runs a single poll cycle immediately, outside of the regular interval.</summary>
    Task<Result<int>> PollOnceAsync(CancellationToken cancellationToken = default);
}
