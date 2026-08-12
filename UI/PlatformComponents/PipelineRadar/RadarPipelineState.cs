namespace MostaqlK.UI.PlatformComponents.PipelineRadar;

/// <summary>Which visual region of the radar the pointer is currently over.</summary>
public enum RadarRegion
{
    None,
    Discovery,
    Queue,
    Worker
}

/// <summary>Visual state of a single enrichment worker segment (inner ring).</summary>
public enum RadarWorkerState
{
    Idle,
    Processing,
    Completed,
    Error
}

/// <summary>
/// The pipeline stage a single project token is currently in. This mirrors the real pipeline
/// (Discovery -> Queue -> Enrichment -> Completion), which is what drives every animation:
/// motion is a consequence of a stage transition, never decoration.
/// </summary>
public enum RadarTokenStage
{
    /// <summary>Detected on the discovery ring, waiting out its (staggered) start delay.</summary>
    Detected,

    /// <summary>Travelling inward from the discovery ring to its queue slot.</summary>
    EnteringQueue,

    /// <summary>Parked on the queue ring; may still slide sideways when the queue reorders.</summary>
    InQueue,

    /// <summary>Travelling from the queue ring to the worker segment that claimed it.</summary>
    MovingToWorker,

    /// <summary>Associated with a worker while it is being enriched.</summary>
    AtWorker,

    /// <summary>Leaving the radar after completion (or removal).</summary>
    Exiting,

    /// <summary>Exit animation finished - the token may be recycled.</summary>
    Done
}

/// <summary>Kind of transient pulse ring drawn on top of the rings.</summary>
public enum RadarPulseKind
{
    /// <summary>Small expanding dot pulse at the detection point on the discovery ring.</summary>
    Detection,

    /// <summary>Arc pulse expanding outward from a worker segment when it finishes.</summary>
    Completion
}

/// <summary>A short-lived expanding/fading pulse. Pooled - never allocated inside the draw loop.</summary>
public sealed class RadarPulse
{
    public RadarPulseKind Kind { get; set; }
    public double Angle { get; set; }
    public double Radius { get; set; }
    public int WorkerIndex { get; set; } = -1;
    public double Progress { get; set; }
    public double Duration { get; set; } = 0.5;
    public bool IsActive { get; set; }
}

/// <summary>
/// A single project moving through the radar. Object identity is preserved for the whole lifetime
/// of the project inside the visualisation (never removed and recreated), so reordering and
/// re-targeting animate from the token's *current* visual position.
/// </summary>
public sealed class RadarProjectToken
{
    public long ProjectId { get; set; }
    public string Title { get; set; } = string.Empty;
    public RadarTokenStage Stage { get; set; }
    public bool IsActive { get; set; }

    /// <summary>Stagger/overlap delay in seconds before the current stage starts moving.</summary>
    public double Delay { get; set; }

    public double Progress { get; set; }
    public double Duration { get; set; } = 0.5;

    public double FromAngle { get; set; }
    public double FromRadius { get; set; }
    public double ToAngle { get; set; }
    public double ToRadius { get; set; }

    /// <summary>Current interpolated position, in degrees / normalised radius (0..1 of the dial).</summary>
    public double Angle { get; set; }
    public double Radius { get; set; }

    public double Opacity { get; set; }
    public double Scale { get; set; } = 1;

    public int WorkerIndex { get; set; } = -1;
    public int QueueSlot { get; set; } = -1;

    /// <summary>Queue value to publish once this token actually lands in the queue ring.</summary>
    public double QueueTargetOnArrival { get; set; } = -1;

    /// <summary>Easing applied to the current stage.</summary>
    public RadarEase Ease { get; set; } = RadarEase.Out;

    public bool IsSelected { get; set; }
    public bool IsHovered { get; set; }
}

/// <summary>Easing curves used by the radar. Deliberately technical - no bounce/elastic.</summary>
public enum RadarEase
{
    Linear,
    Out,
    InOut
}

/// <summary>Per-worker animation state for the inner ring.</summary>
public sealed class RadarWorkerVisual
{
    public RadarWorkerState State { get; set; } = RadarWorkerState.Idle;

    /// <summary>Brightness/thickness driver, smoothly damped toward <see cref="IntensityTarget"/>.</summary>
    public double Intensity { get; set; }
    public double IntensityTarget { get; set; }
    public double IntensityVelocity;

    /// <summary>Breathing (throb) phase in seconds. Offset per worker so they never pulse in unison.</summary>
    public double BreathPhase { get; set; }

    /// <summary>0..1 position of the small highlight travelling through the segment.</summary>
    public double HighlightProgress { get; set; }

    /// <summary>Hover/focus emphasis, smoothly damped (fast - 100..180ms).</summary>
    public double Emphasis { get; set; }
    public double EmphasisTarget { get; set; }
    public double EmphasisVelocity;
}

/// <summary>
/// The single source of truth for everything the radar draws. Nothing here touches MAUI views: the
/// hosting <see cref="PipelineRadar"/> advances this model once per frame from one ticker and the
/// <see cref="PipelineRadarDrawable"/> renders it. All transitions are expressed as
/// current-value -> target-value, which makes every animation interruptible and re-targetable
/// (an update arriving mid-flight redirects instead of restarting or resetting).
/// </summary>
public sealed class RadarPipelineState
{
    /// <summary>Normalised ring radii (fraction of the dial radius).</summary>
    public const double DiscoveryRadius = 0.95;
    public const double QueueRadius = 0.72;
    public const double WorkerRadius = 0.46;

    public const int WorkerCount = 3;
    private const int MaxVisibleTokens = 10;

    // Durations in seconds - the spec's recommended starting points (section 21).
    private const double ScannerPeriod = 4.0;          // one seamless revolution
    private const double EnterQueueDuration = 0.45;
    private const double ReorderDuration = 0.35;
    private const double ToWorkerDuration = 0.65;
    private const double ExitDuration = 0.45;
    private const double DetectionPulseDuration = 0.5;
    private const double CompletionPulseDuration = 0.6;
    private const double HighlightPeriod = 1.7;
    private const double QueueSmoothTime = 0.30;       // ~350-600ms settle, re-targetable
    private const double IntensitySmoothTime = 0.14;
    private const double EmphasisSmoothTime = 0.09;    // ~150ms hover response
    private const double TokenStagger = 0.05;          // 0/50/100/150ms bursts

    private readonly List<RadarProjectToken> _tokens = new(MaxVisibleTokens * 2);
    private readonly List<RadarPulse> _pulses = new(8);
    private int _pendingStaggerSlots;
    private double _staggerResetTimer;

    public IReadOnlyList<RadarProjectToken> Tokens => _tokens;
    public IReadOnlyList<RadarPulse> Pulses => _pulses;

    public RadarWorkerVisual[] Workers { get; } =
    [
        new RadarWorkerVisual { BreathPhase = 0.0 },
        new RadarWorkerVisual { BreathPhase = 0.7 },
        new RadarWorkerVisual { BreathPhase = 1.4 },
    ];

    /// <summary>When true, ambient motion is suppressed and travel is replaced by fades.</summary>
    public bool ReducedMotion { get; set; }

    // --- Discovery tier ---
    public bool IsScanning { get; private set; }
    public double ScannerProgress { get; private set; }
    public double ScannerIntensity { get; private set; }
    private double _scannerIntensityVelocity;
    public double AmbientPhase { get; private set; }

    // --- Snapshot / diff sweep ---
    public bool IsSnapshotActive { get; set; }
    public double SweepProgress { get; private set; }

    // --- Queue tier ---
    /// <summary>Currently *displayed* queue utilisation (0..1) - always interpolated, never snapped.</summary>
    public double QueueDisplayed { get; private set; }
    public double QueueTarget { get; private set; }
    private double _queueVelocity;

    /// <summary>Smoothly interpolated queue item count, for the numeric readout.</summary>
    public double QueueCountDisplayed { get; private set; }
    public double QueueCountTarget { get; private set; }
    private double _queueCountVelocity;

    // --- Interaction ---
    public RadarRegion HoveredRegion { get; private set; }
    public int HoveredWorker { get; private set; } = -1;
    public int FocusedWorker { get; private set; } = -1;

    /// <summary>
    /// A ring the user explicitly *clicked* (discovery or queue), as opposed to merely hovered.
    /// Hover is transient - it disappears the moment the pointer leaves the dial, which is why
    /// clicking the queue ring used to look like it did nothing at all: there was no state for a
    /// click on anything but a worker segment, so no emphasis survived the pointer leaving and no
    /// drill-in stayed pinned. This value is sticky until the same ring (or empty space) is clicked
    /// again, and it feeds the same smoothed emphasis values hover already drives.
    /// </summary>
    public RadarRegion SelectedRegion { get; private set; }

    public double DiscoveryEmphasis { get; private set; }
    private double _discoveryEmphasisTarget;
    private double _discoveryEmphasisVelocity;

    public double QueueEmphasis { get; private set; }
    private double _queueEmphasisTarget;
    private double _queueEmphasisVelocity;

    /// <summary>0..1 amount by which unrelated elements are quietened while focusing/hovering.</summary>
    public double Dimming { get; private set; }
    private double _dimmingTarget;
    private double _dimmingVelocity;

    /// <summary>0..1 reveal of the "queue -> worker" connector drawn in focus mode.</summary>
    public double ConnectorReveal { get; private set; }
    private double _connectorTarget;
    private double _connectorVelocity;

    // ------------------------------------------------------------------ events in

    /// <summary>Scan started/stopped. Never resets the rest of the radar.</summary>
    public void SetScanning(bool scanning) => IsScanning = scanning;

    /// <summary>
    /// Sets the queue utilisation target. The visual value keeps travelling from wherever it is,
    /// so 20 -> 70 and then 70 -> 55 mid-flight simply redirects.
    /// </summary>
    public void SetQueue(double utilisation, int count)
    {
        QueueTarget = Math.Clamp(utilisation, 0, 1);
        QueueCountTarget = Math.Max(0, count);
    }

    /// <summary>
    /// A project was discovered: detection pulse -> token appears on the discovery ring -> token
    /// travels into its queue slot -> the queue ring/number grow once it lands. Bursts are
    /// staggered by 50ms so simultaneous arrivals never move as one block.
    /// </summary>
    public void ProjectDiscovered(long projectId, string title, double queueUtilisationOnArrival, int queueCountOnArrival)
    {
        var angle = ScannerAngle;
        SpawnPulse(RadarPulseKind.Detection, angle, DiscoveryRadius, -1, DetectionPulseDuration);

        var token = RentToken();
        token.ProjectId = projectId;
        token.Title = title;
        token.Stage = RadarTokenStage.Detected;
        token.Delay = _pendingStaggerSlots * TokenStagger;
        token.Progress = 0;
        token.Duration = EnterQueueDuration;
        token.Ease = RadarEase.Out;
        token.Angle = token.FromAngle = token.ToAngle = angle;
        token.Radius = token.FromRadius = token.ToRadius = DiscoveryRadius;
        token.Opacity = 0;
        token.Scale = 0.7;
        token.WorkerIndex = -1;
        token.QueueSlot = -1;
        token.QueueTargetOnArrival = Math.Clamp(queueUtilisationOnArrival, 0, 1);
        QueueCountTarget = Math.Max(QueueCountTarget, queueCountOnArrival - 1);

        _pendingStaggerSlots++;
        _staggerResetTimer = 0.35;

        if (ReducedMotion)
        {
            // No travel: the token fades in on the queue ring and the value applies immediately.
            token.Delay = 0;
            token.Stage = RadarTokenStage.InQueue;
            token.Radius = token.FromRadius = token.ToRadius = QueueRadius;
            token.Opacity = 1;
            token.Scale = 1;
            SetQueue(queueUtilisationOnArrival, queueCountOnArrival);
        }

        AssignQueueSlots();
    }

    /// <summary>
    /// Worker <paramref name="workerIndex"/> claimed a project: the token leaves the queue ring and
    /// travels to that segment. If we have no token for that project (e.g. it was recovered from the
    /// persistent backlog) the oldest queued token is used, so the queue always visibly drains.
    /// </summary>
    public void ProjectAssignedToWorker(int workerIndex, long projectId, string title)
    {
        if (workerIndex < 0 || workerIndex >= WorkerCount)
        {
            return;
        }

        var token = FindToken(projectId) ?? FindOldestQueuedToken();
        var worker = Workers[workerIndex];
        worker.State = RadarWorkerState.Processing;
        worker.IntensityTarget = 1;

        if (token is null)
        {
            return;
        }

        // Release any previous association and re-base the travel from the current position.
        DetachFromWorker(token);
        token.ProjectId = projectId;
        if (!string.IsNullOrEmpty(title))
        {
            token.Title = title;
        }

        token.WorkerIndex = workerIndex;
        token.QueueSlot = -1;
        token.Stage = RadarTokenStage.MovingToWorker;
        token.Delay = 0;
        token.Progress = 0;
        token.Duration = ReducedMotion ? 0.18 : ToWorkerDuration;
        token.Ease = RadarEase.InOut;
        token.FromAngle = token.Angle;
        token.FromRadius = token.Radius;
        token.ToAngle = WorkerCenterAngle(workerIndex);
        token.ToRadius = WorkerRadius;

        AssignQueueSlots();
    }

    /// <summary>Worker finished (or failed): brightness spike, completion pulse, back to idle.</summary>
    public void WorkerStateChanged(int workerIndex, RadarWorkerState state)
    {
        if (workerIndex < 0 || workerIndex >= WorkerCount)
        {
            return;
        }

        var worker = Workers[workerIndex];
        var wasBusy = worker.State == RadarWorkerState.Processing;
        worker.State = state;

        switch (state)
        {
            case RadarWorkerState.Processing:
                worker.IntensityTarget = 1;
                break;

            case RadarWorkerState.Completed:
            case RadarWorkerState.Error:
                worker.IntensityTarget = 1;              // brief brightness increase
                if (!ReducedMotion)
                {
                    SpawnPulse(RadarPulseKind.Completion, WorkerCenterAngle(workerIndex), WorkerRadius, workerIndex, CompletionPulseDuration);
                }

                ExitTokensOfWorker(workerIndex);
                break;

            default:
                worker.IntensityTarget = 0;
                if (wasBusy)
                {
                    ExitTokensOfWorker(workerIndex);
                }

                break;
        }
    }

    /// <summary>A project left the pipeline without a worker (removed/expired) - it animates out.</summary>
    public void ProjectRemoved(long projectId)
    {
        var token = FindToken(projectId);
        if (token is not null)
        {
            StartExit(token);
            AssignQueueSlots();
        }
    }

    public void SetHover(RadarRegion region, int workerIndex)
    {
        HoveredRegion = region;
        HoveredWorker = region == RadarRegion.Worker ? workerIndex : -1;
    }

    /// <summary>Focus mode: the chosen worker dominates, the others (and unrelated rings) quieten.</summary>
    public void SetFocusedWorker(int workerIndex) => FocusedWorker = workerIndex;

    /// <summary>
    /// Pins (or releases, with <see cref="RadarRegion.None"/>) a clicked ring. Worker segments are
    /// expressed through <see cref="FocusedWorker"/> instead, so they are never stored here.
    /// </summary>
    public void SetSelectedRegion(RadarRegion region) =>
        SelectedRegion = region == RadarRegion.Worker ? RadarRegion.None : region;

    public RadarProjectToken? TokenOfWorker(int workerIndex)
    {
        foreach (var token in _tokens)
        {
            if (token.IsActive && token.WorkerIndex == workerIndex &&
                token.Stage is RadarTokenStage.MovingToWorker or RadarTokenStage.AtWorker)
            {
                return token;
            }
        }

        return null;
    }

    public int QueuedTokenCount
    {
        get
        {
            var count = 0;
            foreach (var token in _tokens)
            {
                if (token.IsActive && token.Stage is RadarTokenStage.InQueue or RadarTokenStage.EnteringQueue or RadarTokenStage.Detected)
                {
                    count++;
                }
            }

            return count;
        }
    }

    /// <summary>Scanner head angle in degrees, derived from progress (0..1 -> 0..360).</summary>
    public double ScannerAngle => -90 + (ScannerProgress * 360);

    // ------------------------------------------------------------------ per-frame advance

    /// <summary>
    /// Advances every animated value by <paramref name="dt"/> seconds. Returns true while the radar
    /// still has something to animate, which lets the host park the ticker when fully settled.
    /// </summary>
    public bool Advance(double dt)
    {
        if (dt <= 0)
        {
            return true;
        }

        dt = Math.Min(dt, 0.1); // clamp after a stall so nothing jumps
        var busy = false;

        // --- ambient / discovery ---
        AmbientPhase += dt;
        var scannerTarget = IsScanning ? 1.0 : 0.0;
        ScannerIntensity = SmoothDamp(ScannerIntensity, scannerTarget, ref _scannerIntensityVelocity, IntensitySmoothTime, dt);
        if (Math.Abs(ScannerIntensity - scannerTarget) > 0.002)
        {
            busy = true;
        }

        if (IsScanning && !ReducedMotion)
        {
            // Linear, seamless wrap - the head never jumps back to the start.
            ScannerProgress += dt / ScannerPeriod;
            if (ScannerProgress >= 1)
            {
                ScannerProgress -= Math.Floor(ScannerProgress);
            }

            busy = true;
        }

        if (IsSnapshotActive && !ReducedMotion)
        {
            SweepProgress += dt / 1.5;
            if (SweepProgress >= 1)
            {
                SweepProgress -= Math.Floor(SweepProgress);
            }

            busy = true;
        }

        // --- queue ring + numeric readout (always interpolated, re-targetable) ---
        QueueDisplayed = SmoothDamp(QueueDisplayed, QueueTarget, ref _queueVelocity, QueueSmoothTime, dt);
        if (Math.Abs(QueueDisplayed - QueueTarget) > 0.0008)
        {
            busy = true;
        }

        QueueCountDisplayed = SmoothDamp(QueueCountDisplayed, QueueCountTarget, ref _queueCountVelocity, QueueSmoothTime, dt);
        if (Math.Abs(QueueCountDisplayed - QueueCountTarget) > 0.02)
        {
            busy = true;
        }

        // --- workers ---
        for (var i = 0; i < WorkerCount; i++)
        {
            var worker = Workers[i];
            worker.Intensity = SmoothDamp(worker.Intensity, worker.IntensityTarget, ref worker.IntensityVelocity, IntensitySmoothTime, dt);
            if (Math.Abs(worker.Intensity - worker.IntensityTarget) > 0.002)
            {
                busy = true;
            }

            if (worker.State == RadarWorkerState.Processing && !ReducedMotion)
            {
                worker.BreathPhase += dt;
                worker.HighlightProgress += dt / HighlightPeriod;
                if (worker.HighlightProgress >= 1)
                {
                    worker.HighlightProgress -= Math.Floor(worker.HighlightProgress);
                }

                busy = true;
            }

            worker.EmphasisTarget = EmphasisFor(i);
            worker.Emphasis = SmoothDamp(worker.Emphasis, worker.EmphasisTarget, ref worker.EmphasisVelocity, EmphasisSmoothTime, dt);
            if (Math.Abs(worker.Emphasis - worker.EmphasisTarget) > 0.004)
            {
                busy = true;
            }
        }

        // --- interaction emphasis ---
        // A pinned ring keeps its emphasis after the pointer has left, so a click has a visible,
        // lasting effect instead of vanishing with the hover.
        _discoveryEmphasisTarget = HoveredRegion == RadarRegion.Discovery || SelectedRegion == RadarRegion.Discovery ? 1 : 0;
        _queueEmphasisTarget = HoveredRegion == RadarRegion.Queue || SelectedRegion == RadarRegion.Queue ? 1 : 0;
        _dimmingTarget = FocusedWorker >= 0
            ? 1
            : (HoveredRegion != RadarRegion.None || SelectedRegion != RadarRegion.None ? 0.45 : 0);
        _connectorTarget = FocusedWorker >= 0 && !ReducedMotion ? 1 : 0;

        DiscoveryEmphasis = SmoothDamp(DiscoveryEmphasis, _discoveryEmphasisTarget, ref _discoveryEmphasisVelocity, EmphasisSmoothTime, dt);
        QueueEmphasis = SmoothDamp(QueueEmphasis, _queueEmphasisTarget, ref _queueEmphasisVelocity, EmphasisSmoothTime, dt);
        Dimming = SmoothDamp(Dimming, _dimmingTarget, ref _dimmingVelocity, EmphasisSmoothTime, dt);
        ConnectorReveal = SmoothDamp(ConnectorReveal, _connectorTarget, ref _connectorVelocity, 0.18, dt);

        if (Math.Abs(DiscoveryEmphasis - _discoveryEmphasisTarget) > 0.004 ||
            Math.Abs(QueueEmphasis - _queueEmphasisTarget) > 0.004 ||
            Math.Abs(Dimming - _dimmingTarget) > 0.004 ||
            Math.Abs(ConnectorReveal - _connectorTarget) > 0.004)
        {
            busy = true;
        }

        // --- pulses ---
        for (var i = 0; i < _pulses.Count; i++)
        {
            var pulse = _pulses[i];
            if (!pulse.IsActive)
            {
                continue;
            }

            pulse.Progress += dt / pulse.Duration;
            if (pulse.Progress >= 1)
            {
                pulse.IsActive = false;
                continue;
            }

            busy = true;
        }

        // --- project tokens ---
        if (_staggerResetTimer > 0)
        {
            _staggerResetTimer -= dt;
            if (_staggerResetTimer <= 0)
            {
                _pendingStaggerSlots = 0;
            }
        }

        var slotsChanged = false;
        for (var i = 0; i < _tokens.Count; i++)
        {
            var token = _tokens[i];
            if (!token.IsActive)
            {
                continue;
            }

            if (AdvanceToken(token, dt))
            {
                busy = true;
            }

            if (token.Stage == RadarTokenStage.Done)
            {
                token.IsActive = false;
                slotsChanged = true;
            }
        }

        if (slotsChanged)
        {
            AssignQueueSlots();
        }

        return busy;
    }

    private bool AdvanceToken(RadarProjectToken token, double dt)
    {
        if (token.Delay > 0)
        {
            token.Delay -= dt;
            return true;
        }

        switch (token.Stage)
        {
            case RadarTokenStage.Detected:
                // Overlap with the detection pulse rather than waiting for it to finish.
                token.Stage = RadarTokenStage.EnteringQueue;
                token.Progress = 0;
                token.Duration = ReducedMotion ? 0.2 : EnterQueueDuration;
                token.Ease = RadarEase.Out;
                token.FromAngle = token.Angle;
                token.FromRadius = token.Radius;
                token.ToRadius = QueueRadius;
                token.ToAngle = QueueSlotAngle(token.QueueSlot);
                return true;

            case RadarTokenStage.EnteringQueue:
            {
                token.Progress = Math.Min(1, token.Progress + (dt / token.Duration));
                var t = Ease(token.Ease, token.Progress);
                token.Angle = LerpAngle(token.FromAngle, token.ToAngle, t);
                token.Radius = Lerp(token.FromRadius, token.ToRadius, t);
                token.Opacity = Math.Min(1, token.Progress * 2);
                token.Scale = Lerp(0.7, 1.0, t);

                if (token.Progress >= 1)
                {
                    token.Stage = RadarTokenStage.InQueue;
                    if (token.QueueTargetOnArrival >= 0)
                    {
                        // This is why the number grew: the token landed in the backlog.
                        QueueTarget = token.QueueTargetOnArrival;
                        QueueCountTarget += 1;
                        token.QueueTargetOnArrival = -1;
                    }
                }

                return true;
            }

            case RadarTokenStage.InQueue:
            {
                var target = QueueSlotAngle(token.QueueSlot);
                if (Math.Abs(DeltaAngle(token.Angle, target)) > 0.35)
                {
                    // Reordering: slide from the current position to the new slot, same object.
                    token.Angle = ReducedMotion
                        ? target
                        : LerpAngle(token.Angle, target, Math.Min(1, dt / ReorderDuration * 2.2));
                    token.Opacity = 1;
                    return true;
                }

                token.Opacity = 1;
                token.Scale = token.IsHovered || token.IsSelected ? 1.25 : 1.0;
                return false;
            }

            case RadarTokenStage.MovingToWorker:
            {
                token.Progress = Math.Min(1, token.Progress + (dt / token.Duration));
                var t = Ease(token.Ease, token.Progress);
                token.Angle = LerpAngle(token.FromAngle, token.ToAngle, t);
                token.Radius = Lerp(token.FromRadius, token.ToRadius, t);
                token.Opacity = 1;
                token.Scale = Lerp(1.0, 1.15, t);

                if (token.Progress >= 1)
                {
                    token.Stage = RadarTokenStage.AtWorker;
                }

                return true;
            }

            case RadarTokenStage.AtWorker:
            {
                if (ReducedMotion)
                {
                    token.Scale = 1.1;
                    return false;
                }

                // Stays associated with its worker; only a restrained breathing scale.
                var worker = token.WorkerIndex >= 0 ? Workers[token.WorkerIndex] : null;
                var phase = worker?.BreathPhase ?? AmbientPhase;
                token.Scale = 1.1 + (0.08 * Math.Sin(phase * 2.4));
                token.Opacity = 1;
                return worker?.State == RadarWorkerState.Processing;
            }

            case RadarTokenStage.Exiting:
            {
                token.Progress = Math.Min(1, token.Progress + (dt / token.Duration));
                var t = Ease(RadarEase.Out, token.Progress);
                token.Radius = ReducedMotion ? token.FromRadius : Lerp(token.FromRadius, token.ToRadius, t);
                token.Angle = LerpAngle(token.FromAngle, token.ToAngle, t);
                token.Opacity = 1 - t;
                token.Scale = ReducedMotion ? token.Scale : Lerp(1.1, 0.4, t);

                if (token.Progress >= 1)
                {
                    // Only now is the object released from the collection.
                    token.Stage = RadarTokenStage.Done;
                }

                return true;
            }

            default:
                return false;
        }
    }

    // ------------------------------------------------------------------ helpers

    private double EmphasisFor(int workerIndex)
    {
        if (FocusedWorker == workerIndex)
        {
            return 1;
        }

        if (FocusedWorker >= 0)
        {
            return 0;
        }

        return HoveredRegion == RadarRegion.Worker && HoveredWorker == workerIndex ? 1 : 0;
    }

    private void ExitTokensOfWorker(int workerIndex)
    {
        foreach (var token in _tokens)
        {
            if (token.IsActive && token.WorkerIndex == workerIndex &&
                token.Stage is RadarTokenStage.AtWorker or RadarTokenStage.MovingToWorker)
            {
                StartExit(token);
            }
        }
    }

    private void StartExit(RadarProjectToken token)
    {
        token.Stage = RadarTokenStage.Exiting;
        token.Progress = 0;
        token.Delay = 0;
        token.Duration = ReducedMotion ? 0.2 : ExitDuration;
        token.FromAngle = token.Angle;
        token.FromRadius = token.Radius;
        token.ToAngle = token.Angle;
        token.ToRadius = DiscoveryRadius + 0.2;
        token.QueueSlot = -1;
        token.IsSelected = false;
    }

    private static void DetachFromWorker(RadarProjectToken token) => token.WorkerIndex = -1;

    private RadarProjectToken? FindToken(long projectId)
    {
        foreach (var token in _tokens)
        {
            if (token.IsActive && token.ProjectId == projectId && token.Stage != RadarTokenStage.Exiting)
            {
                return token;
            }
        }

        return null;
    }

    private RadarProjectToken? FindOldestQueuedToken()
    {
        RadarProjectToken? oldest = null;
        foreach (var token in _tokens)
        {
            if (token.IsActive && token.Stage is RadarTokenStage.InQueue or RadarTokenStage.EnteringQueue &&
                (oldest is null || token.QueueSlot < oldest.QueueSlot))
            {
                oldest = token;
            }
        }

        return oldest;
    }

    /// <summary>Recompute queue slots so ordering changes animate instead of teleporting.</summary>
    private void AssignQueueSlots()
    {
        var slot = 0;
        foreach (var token in _tokens)
        {
            if (token.IsActive && token.Stage is RadarTokenStage.InQueue or RadarTokenStage.EnteringQueue or RadarTokenStage.Detected)
            {
                token.QueueSlot = slot++;
            }
        }

        QueueSlotCount = Math.Max(1, slot);
    }

    private int QueueSlotCount { get; set; } = 1;

    private double QueueSlotAngle(int slot)
    {
        if (slot < 0)
        {
            return -90;
        }

        // Spread the queued tokens across the utilisation arc so they read as a backlog.
        var arc = Math.Max(0.12, QueueTarget) * 360;
        var step = arc / Math.Max(1, QueueSlotCount);
        return -90 + (step * (slot + 0.5));
    }

    public static double WorkerCenterAngle(int workerIndex) => -90 + (workerIndex * 120) + 60;

    private RadarProjectToken RentToken()
    {
        foreach (var token in _tokens)
        {
            if (!token.IsActive)
            {
                Reset(token);
                token.IsActive = true;
                return token;
            }
        }

        if (_tokens.Count >= MaxVisibleTokens)
        {
            // Cap the number of live objects: recycle the oldest queued token instead of growing.
            var victim = FindOldestQueuedToken() ?? _tokens[0];
            Reset(victim);
            victim.IsActive = true;
            return victim;
        }

        var created = new RadarProjectToken { IsActive = true };
        _tokens.Add(created);
        return created;
    }

    private static void Reset(RadarProjectToken token)
    {
        token.Stage = RadarTokenStage.Detected;
        token.Progress = 0;
        token.Delay = 0;
        token.WorkerIndex = -1;
        token.QueueSlot = -1;
        token.QueueTargetOnArrival = -1;
        token.Opacity = 0;
        token.Scale = 0.7;
        token.IsSelected = false;
        token.IsHovered = false;
        token.Title = string.Empty;
    }

    private void SpawnPulse(RadarPulseKind kind, double angle, double radius, int workerIndex, double duration)
    {
        RadarPulse? pulse = null;
        foreach (var candidate in _pulses)
        {
            if (!candidate.IsActive)
            {
                pulse = candidate;
                break;
            }
        }

        if (pulse is null)
        {
            if (_pulses.Count >= 8)
            {
                pulse = _pulses[0];
            }
            else
            {
                pulse = new RadarPulse();
                _pulses.Add(pulse);
            }
        }

        pulse.Kind = kind;
        pulse.Angle = angle;
        pulse.Radius = radius;
        pulse.WorkerIndex = workerIndex;
        pulse.Progress = 0;
        pulse.Duration = duration;
        pulse.IsActive = true;
    }

    public static double Ease(RadarEase ease, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return ease switch
        {
            RadarEase.Out => 1 - Math.Pow(1 - t, 3),
            RadarEase.InOut => t < 0.5 ? 4 * t * t * t : 1 - (Math.Pow((-2 * t) + 2, 3) / 2),
            _ => t
        };
    }

    private static double Lerp(double from, double to, double t) => from + ((to - from) * t);

    private static double LerpAngle(double from, double to, double t) => from + (DeltaAngle(from, to) * t);

    private static double DeltaAngle(double from, double to)
    {
        var delta = (to - from) % 360;
        if (delta > 180)
        {
            delta -= 360;
        }
        else if (delta < -180)
        {
            delta += 360;
        }

        return delta;
    }

    /// <summary>
    /// Critically damped approach - the reason a value can be re-targeted mid-flight and still
    /// look like one smooth ease-in-out instead of a restarted animation.
    /// </summary>
    public static double SmoothDamp(double current, double target, ref double velocity, double smoothTime, double dt)
    {
        smoothTime = Math.Max(0.0001, smoothTime);
        var omega = 2.0 / smoothTime;
        var x = omega * dt;
        var exp = 1.0 / (1.0 + x + (0.48 * x * x) + (0.235 * x * x * x));
        var change = current - target;
        var temp = (velocity + (omega * change)) * dt;
        velocity = (velocity - (omega * temp)) * exp;
        return target + ((change + temp) * exp);
    }
}
