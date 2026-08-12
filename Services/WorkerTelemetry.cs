namespace MostaqlK.Services;

/// <summary>
/// Live per-worker figures reported by <see cref="Pipeline.WorkerPool.EnrichmentWorker"/> and shown
/// by the Lighthouse Radar's worker tooltip (status, current project, processing time, completed
/// count, success rate).
/// </summary>
public sealed class WorkerTelemetry
{
    public WorkerTelemetry(int index) => Index = index;

    public int Index { get; }

    public WorkerState State { get; set; } = WorkerState.Idle;

    public long? CurrentProjectId { get; set; }

    public string CurrentProjectTitle { get; set; } = string.Empty;

    /// <summary>Set while the worker is processing; null once it finished.</summary>
    public DateTimeOffset? ProcessingStartedAt { get; set; }

    public int CompletedCount { get; set; }

    public int ErrorCount { get; set; }

    /// <summary>Duration of the most recently finished enrichment, in seconds.</summary>
    public double LastProcessingSeconds { get; set; }

    /// <summary>Seconds spent on the current project, or the last one once idle.</summary>
    public double ElapsedSeconds => ProcessingStartedAt is null
        ? LastProcessingSeconds
        : Math.Max(0, (DateTimeOffset.UtcNow - ProcessingStartedAt.Value).TotalSeconds);

    /// <summary>Share of attempts that completed successfully (1.0 when nothing ran yet).</summary>
    public double SuccessRate
    {
        get
        {
            var total = CompletedCount + ErrorCount;
            return total == 0 ? 1.0 : CompletedCount / (double)total;
        }
    }
}
