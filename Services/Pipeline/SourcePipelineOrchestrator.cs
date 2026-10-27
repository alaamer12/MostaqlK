using MostaqlK.Core.Sources;

namespace MostaqlK.Services.Pipeline;

/// <summary>
/// Builds one <see cref="SourcePipeline"/> per registered source and owns their lifecycle
/// as a group. The UI-facing singletons consumers inject (<c>IPollService</c>,
/// <c>WorkerPool</c>, <c>DiscoveryQueue</c>, the limiter) are the <see cref="Active"/>
/// pipeline's instances, so settings writes and the running engine always address the
/// same objects.
/// </summary>
public sealed class SourcePipelineOrchestrator
{
    private readonly Dictionary<SourceId, SourcePipeline> _bySource = new();
    private readonly ISettingsRepository _settings;
    private readonly PlatformEnablement _enablement;

    public SourcePipelineOrchestrator(
        SourcePipelineFactory factory,
        SourceRegistry registry,
        GlobalAppStatusService globalStatus,
        ISettingsRepository settings,
        PlatformEnablement enablement,
        IEnumerable<ISourceDiscovery> discoveries,
        IEnumerable<ISourceEnrichment> enrichments)
    {
        _settings = settings;
        _enablement = enablement;

        var discoveryBySource = new Dictionary<SourceId, ISourceDiscovery>();
        foreach (var discovery in discoveries)
        {
            discoveryBySource[discovery.Source] = discovery;
        }

        var enrichmentBySource = new Dictionary<SourceId, ISourceEnrichment>();
        foreach (var enrichment in enrichments)
        {
            enrichmentBySource[enrichment.Source] = enrichment;
        }

        foreach (var descriptor in registry.All)
        {
            // A descriptor without its module's discovery+enrichment services cannot run a
            // pipeline; skip it rather than half-wire one (ghost feed/tile class of bug).
            if (discoveryBySource.TryGetValue(descriptor.Id, out var discovery)
                && enrichmentBySource.TryGetValue(descriptor.Id, out var enrichment))
            {
                _bySource[descriptor.Id] = factory.Create(descriptor, discovery, enrichment);
            }
        }

        if (!_bySource.TryGetValue(SourceId.Default, out var active))
        {
            throw new InvalidOperationException($"No pipeline could be built for the default source '{SourceId.Default}'.");
        }

        Active = active;
        globalStatus.Attach(_bySource.Values.Select(p => p.Status), SourceId.Default);
    }

    /// <summary>The pipeline whose telemetry the UI projection follows (single source today).</summary>
    public SourcePipeline Active { get; }

    public IReadOnlyList<SourcePipeline> All => _bySource.Values.ToList();

    public SourcePipeline For(SourceId source) => _bySource.TryGetValue(source, out var pipeline)
        ? pipeline
        : throw new KeyNotFoundException($"No pipeline registered for source '{source}'.");

    /// <summary>
    /// Starts every pipeline's poll + worker loops on the thread pool. Only the caller's
    /// cancellation token governs them (same token as before this refactor); the persisted
    /// interval/pause state seeds the loops exactly like <c>App.StartPipeline</c> used to.
    ///
    /// FIX (whole-window freeze while a fresh database was being filled): both loops must start
    /// via `Task.Run` with no captured UI SynchronizationContext - calling StartAsync directly
    /// made every parse and SQLite write resume on the dispatcher. UI-bound state still reaches
    /// the UI safely because the pipeline statuses marshal their own PropertyChanged.
    /// </summary>
    public void StartAll(CancellationToken pipelineToken)
    {
        var isPollingActive = _settings.GetGlobal(SettingsKeys.IsPollingActive, false);
        var forceDebugPolling = Environment.GetCommandLineArgs().Contains("--debug-via-json");

        foreach (var pipeline in All)
        {
            // The poll interval is a SYSTEM value since the System/Platform split: one number for
            // every platform, read through the flat container. The clamp is the descriptor's
            // (ISourceDescriptor.MinSafePollIntervalSeconds up to SourceCadence's ceiling), because a
            // user who wants mostaql.com checked every 15s must not be able to make nafezly.com answer
            // four requests a minute.
            pipeline.Poll.PollIntervalSeconds = pipeline.Descriptor.ClampPollInterval(
                _settings.GetGlobal(SettingsKeys.PollIntervalSeconds, pipeline.Poll.PollIntervalSeconds));

            // Persist the Start/Pause state: the app now "reserves the state it closed with".
            // Defaults to false (paused) on the very first run to satisfy the "first run not
            // activated" requirement.
            pipeline.Poll.SetPaused(!isPollingActive);

            // Seed the platform switch the same way the cadence is seeded: a platform that was off
            // when the app closed must still be off after it reopens, without the settings page ever
            // having to be opened to say so. Logged because "why is this platform quiet?" is otherwise
            // indistinguishable from "this platform is slow" in the field.
            var platformEnabled = _enablement.IsEnabled(pipeline.Source);
            SetPlatformEnabled(pipeline.Source, platformEnabled);
            MostaqlK.Services.Diagnostics.InteractionLogger.Mark("Pipeline.PlatformGate", "A", new
            {
                source = pipeline.Source.Value,
                enabled = platformEnabled,
            });

            // If debug-via-json is requested, force-enable polling to ensure we have data to export.
            if (forceDebugPolling)
            {
                pipeline.Poll.SetPaused(false);
            }

            _ = Task.Run(() => pipeline.Poll.StartAsync(pipelineToken), pipelineToken);
            _ = Task.Run(() => pipeline.Pool.StartAsync(pipelineToken), pipelineToken);
        }
    }

    /// <summary>
    /// Applies one platform's switched-on/off state to its running pipeline, live and without a
    /// restart: the poll loop stops contacting the site (a forced "check now" included), the worker
    /// pool stops dequeuing enrichment (holding queued items rather than dropping them), and the
    /// status projection reports the reason. Navigation, stored data and the unread badge are
    /// deliberately untouched — see <see cref="PlatformEnablement"/>.
    /// </summary>
    public void SetPlatformEnabled(SourceId source, bool enabled)
    {
        var pipeline = For(source);
        pipeline.Poll.PlatformEnabled = enabled;
        pipeline.Pool.SetPaused(!enabled);
        pipeline.Status.IsPlatformDisabled = !enabled;
    }
}
