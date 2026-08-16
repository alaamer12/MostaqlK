using MostaqlK.Core.Formatting;
using MostaqlK.Infrastructure.Database;

namespace MostaqlK.Services;

/// <summary>
/// Background service that periodically updates the relative publication time for all projects
/// in the database. Runs once on startup and then every 1 minute.
/// </summary>
public sealed class PublishedTimeUpdateService : IDisposable
{
    private readonly IProjectRepository _projectRepository;
    private readonly CancellationTokenSource _cts = new();
    private Task? _timerTask;

    public PublishedTimeUpdateService(IProjectRepository projectRepository)
    {
        _projectRepository = projectRepository;
    }

    public void Start()
    {
        _timerTask = RunPeriodicUpdatesAsync(_cts.Token);
    }

    private async Task RunPeriodicUpdatesAsync(CancellationToken cancellationToken)
    {
        // Run once immediately on startup.
        await UpdateAllPublishedTimesAsync(cancellationToken);

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                await UpdateAllPublishedTimesAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
    }

    private async Task UpdateAllPublishedTimesAsync(CancellationToken cancellationToken)
    {
        var result = await _projectRepository.GetAllProjectTimestampsAsync(cancellationToken);
        if (!result.IsOk)
        {
            return;
        }

        var log = new System.Text.StringBuilder();
        log.AppendLine($"--- Update Cycle: {DateTimeOffset.UtcNow:O} ---");
        log.AppendLine($"Found {result.Value.Count} projects.");

        foreach (var (projectId, discoveredAt) in result.Value)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            var (number, text) = ArabicRelativeTime.GetRelative(discoveredAt);
            log.AppendLine($"Project {projectId}: Discovered {discoveredAt:O}, Elapsed {DateTimeOffset.UtcNow - discoveredAt}, Result: {number} | {text}");
            await _projectRepository.UpdatePublishedTimeAsync(projectId, number, text, cancellationToken);
        }

        try
        {
            Directory.CreateDirectory("scratch");
            File.AppendAllText("scratch/service_log.txt", log.ToString());
        }
        catch { }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}
