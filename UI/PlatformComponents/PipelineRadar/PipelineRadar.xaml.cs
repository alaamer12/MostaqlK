using System.Globalization;
using MostaqlK.Core.Formatting;
using MostaqlK.Services;

namespace MostaqlK.UI.PlatformComponents.PipelineRadar;

/// <summary>
/// The Lighthouse Radar: a single state-driven visualisation of the
/// Discovery -> Queue -> Enrichment -> Completion pipeline.
/// <para>
/// There is exactly one animation mechanism here: a single frame ticker that advances
/// <see cref="RadarPipelineState"/> and invalidates the canvas once per frame. Pipeline events only
/// change *targets* on that state, which is what makes every transition interruptible and
/// re-targetable - an update arriving mid-animation redirects the motion instead of resetting the
/// radar. The ticker parks itself as soon as the state is fully settled, so an idle pipeline costs
/// nothing, and it honours the platform's reduced-motion preference.
/// </para>
/// </summary>
public partial class PipelineRadar : ContentView
{
    private const string TickerName = "RadarTicker";
    private const double TooltipDuration = 180;      // fast, per spec section 14
    private const double IdleGraceSeconds = 0.4;

    private readonly RadarPipelineState _state = new();
    private readonly PipelineRadarDrawable _drawable;
    private readonly System.Diagnostics.Stopwatch _clock = new();

    private GlobalAppStatusService? _status;
    private bool _isTicking;
    private bool _isFrozen;
    private double _lastTickSeconds;
    private double _idleGrace;

    private RadarRegion _pointerRegion = RadarRegion.None;
    private int _pointerWorker = -1;
    private CancellationTokenSource? _tooltipCts;

    // Tweened tooltip figures, so displayed numbers never snap between two values.
    private double _queueCountShown;
    private double _queueCountVelocity;
    private double _utilisationShown;
    private double _utilisationVelocity;
    private double _processingSecondsShown;
    private double _processingSecondsVelocity;

    public PipelineRadar()
    {
        InitializeComponent();
        _drawable = new PipelineRadarDrawable(_state);
        RadarCanvas.Drawable = _drawable;
        _state.ReducedMotion = MotionPreferences.IsReducedMotionRequested;
        ApplyTheme();
        ApplyDiameter(Diameter);

        // Colours are computed in C#, so they have to be re-applied when the theme flips.
        if (Application.Current is not null)
        {
            Application.Current.RequestedThemeChanged += OnRequestedThemeChanged;
        }
    }

    private void OnRequestedThemeChanged(object? sender, AppThemeChangedEventArgs e)
    {
        ApplyTheme();
        RadarCanvas.Invalidate();
    }

    private void ApplyTheme() =>
        _drawable.IsDarkTheme = (Application.Current?.RequestedTheme ?? AppTheme.Dark) == AppTheme.Dark;

    /// <summary>
    /// Stops the ticker and all canvas invalidation without losing any state. Hosts call this while
    /// they animate the radar's own layout (the dashboard panel's collapse slide): a GraphicsView
    /// redrawing at 60fps inside a view whose size changes every frame forces a measure pass per
    /// frame, which is what made the slide hop.
    /// </summary>
    public void FreezeRendering()
    {
        _isFrozen = true;
        _isTicking = false;
        this.AbortAnimation(TickerName);
    }

    /// <summary>Resumes rendering after <see cref="FreezeRendering"/> and catches the dial up.</summary>
    public void ResumeRendering()
    {
        if (!_isFrozen)
        {
            return;
        }

        _isFrozen = false;
        RadarCanvas.Invalidate();
        Wake();
    }

    // ------------------------------------------------------------------ state read by hosts

    /// <summary>Raised whenever the hovered ring/worker changes (-1 when no worker is hovered).</summary>
    public event Action<RadarRegion, int>? HoverChanged;

    /// <summary>Raised when the focused worker changes; -1 means focus was released.</summary>
    public event Action<int>? FocusedWorkerChanged;

    /// <summary>The worker the user clicked to focus, or -1.</summary>
    public int FocusedWorker => _state.FocusedWorker;

    /// <summary>
    /// Raised when the user's explicit selection changes, with the pinned ring and the focused
    /// worker (-1 when no worker is focused). Hosts use it to pin their drill-in to the clicked
    /// ring instead of following the pointer only.
    /// </summary>
    public event Action<RadarRegion, int>? SelectionChanged;

    /// <summary>The ring the user clicked to pin, or <see cref="RadarRegion.None"/>.</summary>
    public RadarRegion SelectedRegion => _state.SelectedRegion;

    /// <summary>The ring currently under the pointer.</summary>
    public RadarRegion HoveredRegion => _pointerRegion;

    /// <summary>The hovered worker index, or -1.</summary>
    public int HoveredWorker => _pointerWorker;

    /// <summary>True while a listing scan is in flight.</summary>
    public bool IsScanning => _state.IsScanning;

    /// <summary>Backlog size as currently *displayed* - already interpolated, so it never snaps.</summary>
    public double DisplayedQueueCount => _state.QueueCountDisplayed;

    /// <summary>Backlog utilisation (0..1) as currently displayed.</summary>
    public double DisplayedUtilisation => _state.QueueDisplayed;

    /// <summary>Visual state of a worker segment.</summary>
    public RadarWorkerState WorkerStateAt(int index) =>
        index >= 0 && index < RadarPipelineState.WorkerCount
            ? _state.Workers[index].State
            : RadarWorkerState.Idle;

    /// <summary>Title of the project token currently associated with a worker, if any.</summary>
    public string? ProjectTitleOfWorker(int index) => _state.TokenOfWorker(index)?.Title;

    /// <summary>How many project tokens the dial is currently drawing on the queue ring.</summary>
    public int VisibleQueuedTokens => _state.QueuedTokenCount;

    /// <summary>Titles of the projects the dial currently shows sitting in the queue ring.</summary>
    public IEnumerable<string> QueuedProjectTitles
    {
        get
        {
            foreach (var token in _state.Tokens)
            {
                if (token.IsActive && token.Stage == RadarTokenStage.InQueue && !string.IsNullOrEmpty(token.Title))
                {
                    yield return token.Title;
                }
            }
        }
    }

    /// <summary>Focuses a worker (or releases focus with -1) from outside the dial.</summary>
    public void FocusWorker(int index)
    {
        if (_state.FocusedWorker == index)
        {
            return;
        }

        _state.SetFocusedWorker(index);
        // A worker and a ring are mutually exclusive selections: focusing a worker releases a
        // pinned ring so the host's drill-in never has two competing sources.
        _state.SetSelectedRegion(RadarRegion.None);
        SelectTokenOfFocusedWorker();
        FocusedWorkerChanged?.Invoke(_state.FocusedWorker);
        SelectionChanged?.Invoke(_state.SelectedRegion, _state.FocusedWorker);
        Wake();
    }

    private void ApplyDiameter(double diameter)
    {
        var size = Math.Max(24, diameter);
        if (Math.Abs(RadarBox.WidthRequest - size) < 0.5)
        {
            // Re-requesting the same size still queues a layout pass, and hosts drive this from
            // SizeChanged - so the no-op case has to actually be a no-op.
            return;
        }

        RadarBox.WidthRequest = size;
        RadarBox.HeightRequest = size;
        RadarCanvas.Invalidate();
    }

    private void ApplyShowTooltip(bool show)
    {
        if (show)
        {
            return;
        }

        _tooltipCts?.Cancel();
        TooltipPanel.Opacity = 0;
        TooltipPanel.IsVisible = false;
    }

    // ------------------------------------------------------------------ bindable pipeline inputs

    public static readonly BindableProperty DiscoveryProgressProperty =
        BindableProperty.Create(nameof(DiscoveryProgress), typeof(double), typeof(PipelineRadar), 0.0,
            propertyChanged: (b, _, n) => ((PipelineRadar)b).OnDiscoveryProgressChanged((double)n));

    /// <summary>0 while idle, &gt;0 while a listing scan is in flight.</summary>
    public double DiscoveryProgress
    {
        get => (double)GetValue(DiscoveryProgressProperty);
        set => SetValue(DiscoveryProgressProperty, value);
    }

    public static readonly BindableProperty QueuePressureProperty =
        BindableProperty.Create(nameof(QueuePressure), typeof(double), typeof(PipelineRadar), 0.0,
            propertyChanged: (b, _, _) => ((PipelineRadar)b).OnQueueChanged());

    /// <summary>Backlog utilisation (0..1). The ring always travels toward this value.</summary>
    public double QueuePressure
    {
        get => (double)GetValue(QueuePressureProperty);
        set => SetValue(QueuePressureProperty, value);
    }

    public static readonly BindableProperty QueueCountProperty =
        BindableProperty.Create(nameof(QueueCount), typeof(int), typeof(PipelineRadar), 0,
            propertyChanged: (b, _, _) => ((PipelineRadar)b).OnQueueChanged());

    /// <summary>Number of items in the backlog, shown by the queue tooltip.</summary>
    public int QueueCount
    {
        get => (int)GetValue(QueueCountProperty);
        set => SetValue(QueueCountProperty, value);
    }

    public static readonly BindableProperty QueueCapacityProperty =
        BindableProperty.Create(nameof(QueueCapacity), typeof(int), typeof(PipelineRadar), 50);

    /// <summary>Backlog capacity the utilisation percentage is measured against.</summary>
    public int QueueCapacity
    {
        get => (int)GetValue(QueueCapacityProperty);
        set => SetValue(QueueCapacityProperty, value);
    }

    public static readonly BindableProperty EnrichmentActivityProperty =
        BindableProperty.Create(nameof(EnrichmentActivity), typeof(double), typeof(PipelineRadar), 0.0,
            propertyChanged: (b, _, n) => ((PipelineRadar)b).OnEnrichmentActivityChanged((double)n));

    /// <summary>
    /// Coarse fallback signal (share of busy workers) for hosts that do not report per-worker state.
    /// Detailed <see cref="WorkerState"/> events always win over this.
    /// </summary>
    public double EnrichmentActivity
    {
        get => (double)GetValue(EnrichmentActivityProperty);
        set => SetValue(EnrichmentActivityProperty, value);
    }

    public static readonly BindableProperty DiameterProperty =
        BindableProperty.Create(nameof(Diameter), typeof(double), typeof(PipelineRadar), 56.0,
            propertyChanged: (b, _, n) => ((PipelineRadar)b).ApplyDiameter((double)n));

    /// <summary>
    /// Edge length of the square dial. The drawable scales everything from the canvas rect, so the
    /// same unit serves both a small inline dial and the large dial in the pipeline dashboard panel.
    /// </summary>
    public double Diameter
    {
        get => (double)GetValue(DiameterProperty);
        set => SetValue(DiameterProperty, value);
    }

    public static readonly BindableProperty ShowTooltipProperty =
        BindableProperty.Create(nameof(ShowTooltip), typeof(bool), typeof(PipelineRadar), true,
            propertyChanged: (b, _, n) => ((PipelineRadar)b).ApplyShowTooltip((bool)n));

    /// <summary>
    /// Whether hovering shows the built-in overflowing data panel. Hosts that render the same
    /// figures themselves (the pipeline dashboard panel) turn it off instead of stacking two
    /// readouts on top of each other; hover emphasis and <see cref="HoverChanged"/> still work.
    /// </summary>
    public bool ShowTooltip
    {
        get => (bool)GetValue(ShowTooltipProperty);
        set => SetValue(ShowTooltipProperty, value);
    }

    public static readonly BindableProperty IsSnapshotActiveProperty =
        BindableProperty.Create(nameof(IsSnapshotActive), typeof(bool), typeof(PipelineRadar), false,
            propertyChanged: (b, _, n) => ((PipelineRadar)b).OnSnapshotChanged((bool)n));

    /// <summary>True while the diff engine holds a point-in-time snapshot (radial sweep).</summary>
    public bool IsSnapshotActive
    {
        get => (bool)GetValue(IsSnapshotActiveProperty);
        set => SetValue(IsSnapshotActiveProperty, value);
    }

    // ------------------------------------------------------------------ service wiring

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();

        if (Handler is null)
        {
            Detach();
            return;
        }

        _state.ReducedMotion = MotionPreferences.IsReducedMotionRequested;
        Attach(IPlatformApplication.Current?.Services.GetService<GlobalAppStatusService>());
        Wake();
    }

    private void Attach(GlobalAppStatusService? status)
    {
        if (status is null || ReferenceEquals(status, _status))
        {
            return;
        }

        Detach();
        _status = status;
        status.ProjectDiscovered += OnProjectDiscovered;
        status.ProjectAssignedToWorker += OnProjectAssignedToWorker;
        status.ProjectRemovedFromQueue += OnProjectRemovedFromQueue;
        status.WorkerStateChanged += OnWorkerStateChanged;
    }

    private void Detach()
    {
        if (_status is null)
        {
            return;
        }

        _status.ProjectDiscovered -= OnProjectDiscovered;
        _status.ProjectAssignedToWorker -= OnProjectAssignedToWorker;
        _status.ProjectRemovedFromQueue -= OnProjectRemovedFromQueue;
        _status.WorkerStateChanged -= OnWorkerStateChanged;
        _status = null;
        _isTicking = false;
    }

    // ------------------------------------------------------------------ pipeline events in

    private void OnProjectDiscovered(long projectId, string title) => OnMainThread(() =>
    {
        // The queue ring/number only grow once the token actually lands in the backlog, so the
        // motion explains *why* the value changed instead of the number jumping on its own.
        var capacity = Math.Max(1, QueueCapacityOrDefault);
        var arrivalCount = Math.Max(QueueCount, _state.QueuedTokenCount + 1);
        _state.ProjectDiscovered(projectId, title, Math.Min(1.0, arrivalCount / (double)capacity), arrivalCount);
        Wake();
    });

    private void OnProjectAssignedToWorker(int workerIndex, long projectId, string title) => OnMainThread(() =>
    {
        _state.ProjectAssignedToWorker(workerIndex, projectId, title);
        Wake();
    });

    private void OnProjectRemovedFromQueue(long projectId) => OnMainThread(() =>
    {
        _state.ProjectRemoved(projectId);
        Wake();
    });

    private void OnWorkerStateChanged(int workerIndex, WorkerState state) => OnMainThread(() =>
    {
        _state.WorkerStateChanged(workerIndex, state switch
        {
            WorkerState.Processing => RadarWorkerState.Processing,
            WorkerState.Completed => RadarWorkerState.Completed,
            WorkerState.Error => RadarWorkerState.Error,
            _ => RadarWorkerState.Idle
        });

        Wake();
    });

    // The pipeline runs on background threads and these bindings are updated from there, so every
    // entry point marshals onto the UI thread: the state model and the ticker are single-threaded.
    private void OnDiscoveryProgressChanged(double progress) => OnMainThread(() =>
    {
        _state.SetScanning(progress > 0);
        Wake();
    });

    private void OnQueueChanged() => OnMainThread(() =>
    {
        var capacity = Math.Max(1, QueueCapacityOrDefault);
        var utilisation = QueueCount > 0 ? Math.Min(1.0, QueueCount / (double)capacity) : QueuePressure;
        _state.SetQueue(utilisation, QueueCount);
        Wake();
    });

    private void OnEnrichmentActivityChanged(double activity) => OnMainThread(() =>
    {
        // Only used as a fallback: never override a worker that reported a detailed state.
        var busy = (int)Math.Round(Math.Clamp(activity, 0, 1) * RadarPipelineState.WorkerCount);
        for (var i = 0; i < RadarPipelineState.WorkerCount; i++)
        {
            var reported = _status?.WorkerStates[i] ?? WorkerState.Idle;
            if (reported != WorkerState.Idle)
            {
                continue;
            }

            _state.WorkerStateChanged(i, i < busy ? RadarWorkerState.Processing : RadarWorkerState.Idle);
        }

        Wake();
    });

    private void OnSnapshotChanged(bool active) => OnMainThread(() =>
    {
        _state.IsSnapshotActive = active;
        Wake();
    });

    private int QueueCapacityOrDefault => _status?.QueueCapacity > 0 ? _status.QueueCapacity : QueueCapacity;

    // ------------------------------------------------------------------ the single ticker

    /// <summary>
    /// Starts (or keeps) the one animation loop that advances the whole radar. Called by every
    /// state change - no other timers exist, and the loop stops on its own once nothing moves.
    /// </summary>
    private void Wake()
    {
        _idleGrace = IdleGraceSeconds;

        if (_isFrozen || _isTicking || Handler is null)
        {
            return;
        }

        if (this.AnimationIsRunning(TickerName))
        {
            // The previous loop has not wound down yet - just re-arm it instead of committing a
            // second animation under the same name.
            _isTicking = true;
            return;
        }

        _isTicking = true;
        _clock.Restart();
        _lastTickSeconds = 0;

        new Animation(_ => OnFrame())
            .Commit(this, TickerName, rate: 16, length: 1000, repeat: () => _isTicking);
    }

    private void OnFrame()
    {
        var now = _clock.Elapsed.TotalSeconds;
        var dt = now - _lastTickSeconds;
        _lastTickSeconds = now;

        var busy = _state.Advance(dt);
        UpdateTooltipValues(dt);
        RadarCanvas.Invalidate();

        if (busy || _pointerRegion != RadarRegion.None)
        {
            _idleGrace = IdleGraceSeconds;
            return;
        }

        _idleGrace -= dt;
        if (_idleGrace <= 0)
        {
            // Fully settled: park the loop instead of burning frames on a static picture.
            _isTicking = false;
        }
    }

    private static void OnMainThread(Action action)
    {
        if (MainThread.IsMainThread)
        {
            action();
        }
        else
        {
            MainThread.BeginInvokeOnMainThread(action);
        }
    }

    // ------------------------------------------------------------------ interaction

    private void OnPointerEntered(object? sender, PointerEventArgs e) => UpdatePointer(e);

    private void OnPointerMoved(object? sender, PointerEventArgs e) => UpdatePointer(e);

    private void OnPointerExited(object? sender, PointerEventArgs e)
    {
        _pointerRegion = RadarRegion.None;
        _pointerWorker = -1;
        _state.SetHover(RadarRegion.None, -1);
        ClearTokenHover();
        _ = HideTooltipAsync();
        HoverChanged?.Invoke(RadarRegion.None, -1);
        Wake();
    }

    private void UpdatePointer(PointerEventArgs e)
    {
        var position = e.GetPosition(RadarCanvas);
        if (position is null)
        {
            return;
        }

        var region = HitTest(position.Value, out var workerIndex);
        if (region == _pointerRegion && workerIndex == _pointerWorker)
        {
            return;
        }

        _pointerRegion = region;
        _pointerWorker = workerIndex;
        _state.SetHover(region, workerIndex);
        HoverNearestToken(region, workerIndex);

        if (region == RadarRegion.None)
        {
            _ = HideTooltipAsync();
        }
        else
        {
            ResetTooltipTweens(region, workerIndex);
            UpdateTooltipText(region, workerIndex);
            _ = ShowTooltipAsync();
        }

        HoverChanged?.Invoke(region, workerIndex);
        Wake();
    }

    private void OnTapped(object? sender, TappedEventArgs e)
    {
        var pos = e.GetPosition(RadarCanvas);
        var region = _pointerRegion;
        var worker = _pointerWorker;

        if (pos.HasValue)
        {
            var tapRegion = HitTest(pos.Value, out var tapWorker);
            if (tapRegion != RadarRegion.None || region == RadarRegion.None)
            {
                region = tapRegion;
                worker = tapWorker;
            }
        }

        // On touch screens where hover events do not occur, update hover & tooltip on tap
        _pointerRegion = region;
        _pointerWorker = worker;
        _state.SetHover(region, worker);
        HoverNearestToken(region, worker);

        if (region == RadarRegion.None)
        {
            _ = HideTooltipAsync();
        }
        else
        {
            ResetTooltipTweens(region, worker);
            UpdateTooltipText(region, worker);
            _ = ShowTooltipAsync();
        }

        HoverChanged?.Invoke(region, worker);

        // Click a worker segment to focus it (and its project), click the discovery or queue ring to
        // pin that ring's drill-in, click the same thing again - or empty space - to release.
        if (region == RadarRegion.Worker && worker >= 0)
        {
            _state.SetSelectedRegion(RadarRegion.None);
            _state.SetFocusedWorker(_state.FocusedWorker == worker ? -1 : worker);
        }
        else if (region is RadarRegion.Queue or RadarRegion.Discovery)
        {
            _state.SetFocusedWorker(-1);
            _state.SetSelectedRegion(_state.SelectedRegion == region ? RadarRegion.None : region);
        }
        else
        {
            _state.SetFocusedWorker(-1);
            _state.SetSelectedRegion(RadarRegion.None);
        }

        SelectTokenOfFocusedWorker();
        SelectTokensOfSelectedRegion();
        FocusedWorkerChanged?.Invoke(_state.FocusedWorker);
        SelectionChanged?.Invoke(_state.SelectedRegion, _state.FocusedWorker);
        Wake();
    }

    private RadarRegion HitTest(Point position, out int workerIndex)
    {
        workerIndex = -1;

        var size = Math.Min(RadarCanvas.Width, RadarCanvas.Height);
        if (size <= 0)
        {
            return RadarRegion.None;
        }

        var radius = (size / 2) - 4;
        var dx = position.X - (RadarCanvas.Width / 2);
        var dy = position.Y - (RadarCanvas.Height / 2);
        var distance = Math.Sqrt((dx * dx) + (dy * dy)) / radius;

        if (distance > RadarPipelineState.DiscoveryRadius + 0.18)
        {
            return RadarRegion.None;
        }

        if (distance > (RadarPipelineState.DiscoveryRadius + RadarPipelineState.QueueRadius) / 2)
        {
            return RadarRegion.Discovery;
        }

        if (distance > (RadarPipelineState.QueueRadius + RadarPipelineState.WorkerRadius) / 2)
        {
            return RadarRegion.Queue;
        }

        var angle = (Math.Atan2(dy, dx) * 180 / Math.PI) + 90; // 0 at 12 o'clock, clockwise
        if (angle < 0)
        {
            angle += 360;
        }

        workerIndex = Math.Clamp((int)(angle / 120), 0, RadarPipelineState.WorkerCount - 1);
        return RadarRegion.Worker;
    }

    private void HoverNearestToken(RadarRegion region, int workerIndex)
    {
        foreach (var token in _state.Tokens)
        {
            token.IsHovered = token.IsActive && region switch
            {
                RadarRegion.Queue => token.Stage == RadarTokenStage.InQueue,
                RadarRegion.Worker => token.WorkerIndex == workerIndex,
                _ => false
            };
        }
    }

    private void ClearTokenHover()
    {
        foreach (var token in _state.Tokens)
        {
            token.IsHovered = false;
        }
    }

    private void SelectTokenOfFocusedWorker()
    {
        foreach (var token in _state.Tokens)
        {
            token.IsSelected = _state.FocusedWorker >= 0 && token.WorkerIndex == _state.FocusedWorker;
        }
    }

    /// <summary>
    /// Pinning the queue ring also marks every queued token as selected, so the click visibly
    /// answers "which items is this arc made of?" rather than only brightening the arc.
    /// </summary>
    private void SelectTokensOfSelectedRegion()
    {
        if (_state.SelectedRegion != RadarRegion.Queue)
        {
            return;
        }

        foreach (var token in _state.Tokens)
        {
            token.IsSelected = token.IsActive && token.Stage == RadarTokenStage.InQueue;
        }
    }

    // ------------------------------------------------------------------ tooltip / data panel

    private async Task ShowTooltipAsync()
    {
        _tooltipCts?.Cancel();
        var cts = new CancellationTokenSource();
        _tooltipCts = cts;

        try
        {
            if (!ShowTooltip)
            {
                return;
            }

            TooltipPanel.IsVisible = true;
            if (_state.ReducedMotion)
            {
                TooltipPanel.TranslationY = 0;
                TooltipPanel.Opacity = 1;
                return;
            }

            if (TooltipPanel.Opacity <= 0.01)
            {
                TooltipPanel.TranslationY = -6;
            }

            // Short fade + small translation - never an instantly appearing panel.
            await Task.WhenAll(
                TooltipPanel.FadeToAsync(1, (uint)TooltipDuration, Easing.CubicOut),
                TooltipPanel.TranslateToAsync(0, 0, (uint)TooltipDuration, Easing.CubicOut));
        }
        catch (TaskCanceledException)
        {
            // Superseded by a newer hover - the panel keeps its current visual state.
        }
        finally
        {
            if (ReferenceEquals(_tooltipCts, cts))
            {
                _tooltipCts = null;
            }

            cts.Dispose();
        }
    }

    private async Task HideTooltipAsync()
    {
        _tooltipCts?.Cancel();
        var cts = new CancellationTokenSource();
        _tooltipCts = cts;

        try
        {
            if (_state.ReducedMotion)
            {
                TooltipPanel.Opacity = 0;
                TooltipPanel.IsVisible = false;
                return;
            }

            await Task.WhenAll(
                TooltipPanel.FadeToAsync(0, (uint)TooltipDuration, Easing.CubicOut),
                TooltipPanel.TranslateToAsync(0, -6, (uint)TooltipDuration, Easing.CubicOut));

            if (!cts.IsCancellationRequested)
            {
                TooltipPanel.IsVisible = false;
            }
        }
        catch (TaskCanceledException)
        {
            // A new hover arrived mid-fade; the show animation takes over from here.
        }
        finally
        {
            if (ReferenceEquals(_tooltipCts, cts))
            {
                _tooltipCts = null;
            }

            cts.Dispose();
        }
    }

    private void ResetTooltipTweens(RadarRegion region, int workerIndex)
    {
        if (region == RadarRegion.Queue)
        {
            _queueCountShown = _state.QueueCountDisplayed;
            _utilisationShown = _state.QueueDisplayed * 100;
        }
        else if (region == RadarRegion.Worker && workerIndex >= 0 && _status is not null)
        {
            _processingSecondsShown = _status.Workers[workerIndex].ElapsedSeconds;
        }
    }

    /// <summary>Interpolates the displayed figures so numbers glide instead of snapping.</summary>
    private void UpdateTooltipValues(double dt)
    {
        if (_pointerRegion == RadarRegion.None)
        {
            return;
        }

        _queueCountShown = RadarPipelineState.SmoothDamp(_queueCountShown, _state.QueueCountDisplayed, ref _queueCountVelocity, 0.25, dt);
        _utilisationShown = RadarPipelineState.SmoothDamp(_utilisationShown, _state.QueueDisplayed * 100, ref _utilisationVelocity, 0.25, dt);

        if (_pointerRegion == RadarRegion.Worker && _pointerWorker >= 0 && _status is not null)
        {
            _processingSecondsShown = RadarPipelineState.SmoothDamp(
                _processingSecondsShown, _status.Workers[_pointerWorker].ElapsedSeconds, ref _processingSecondsVelocity, 0.25, dt);
        }

        if (ShowTooltip)
        {
            UpdateTooltipText(_pointerRegion, _pointerWorker);
        }
    }

    private void UpdateTooltipText(RadarRegion region, int workerIndex)
    {
        switch (region)
        {
            case RadarRegion.Discovery:
                SetTooltip(
                    "الاستكشاف",
                    _state.IsScanning ? "الحالة: جارٍ الفحص" : "الحالة: خامل",
                    LastScanText.Labelled(_status?.LastScanCompletedAt),
                    $"مشاريع مكتشفة: {_status?.ProjectsDiscoveredCount ?? 0}",
                    $"فترة الفحص: {_status?.ScanIntervalSeconds ?? 0} ث",
                    string.Empty);
                break;

            case RadarRegion.Queue:
                SetTooltip(
                    "قائمة الانتظار",
                    $"{Math.Round(_queueCountShown)} / {QueueCapacityOrDefault}",
                    $"{Math.Round(_utilisationShown)}% استخدام",
                    $"أقدم عنصر: {Format(_status?.OldestQueuedItemSeconds ?? 0)} ث",
                    $"متوسط الانتظار: {Format(_status?.AverageQueueWaitSeconds ?? 0)} ث",
                    string.Empty);
                break;

            case RadarRegion.Worker when workerIndex >= 0:
                var telemetry = _status?.Workers[workerIndex];
                var token = _state.TokenOfWorker(workerIndex);
                var project = !string.IsNullOrEmpty(telemetry?.CurrentProjectTitle)
                    ? telemetry!.CurrentProjectTitle
                    : !string.IsNullOrEmpty(token?.Title) ? token!.Title : "-";

                SetTooltip(
                    $"العامل {workerIndex + 1}",
                    $"الحالة: {StatusText(_state.Workers[workerIndex].State)}",
                    $"المشروع الحالي: {project}",
                    $"مدة المعالجة: {Format(_processingSecondsShown)} ث",
                    $"مكتملة: {telemetry?.CompletedCount ?? 0}",
                    $"نسبة النجاح: {Math.Round((telemetry?.SuccessRate ?? 1) * 100)}%");
                break;
        }
    }

    private void SetTooltip(string title, string line1, string line2, string line3, string line4, string line5)
    {
        // Only assign when the text actually changed - avoids needless layout passes every frame.
        Assign(TooltipTitle, title);
        Assign(TooltipLine1, line1);
        Assign(TooltipLine2, line2);
        Assign(TooltipLine3, line3);
        Assign(TooltipLine4, line4);
        Assign(TooltipLine5, line5);

        static void Assign(Label label, string text)
        {
            if (!string.Equals(label.Text, text, StringComparison.Ordinal))
            {
                label.Text = text;
            }

            var visible = !string.IsNullOrEmpty(text);
            if (label.IsVisible != visible)
            {
                label.IsVisible = visible;
            }
        }
    }

    private static string StatusText(RadarWorkerState state) =>
        PipelineTelemetryFormatter.FormatWorkerState(state);

    private static string Format(double seconds) =>
        PipelineTelemetryFormatter.FormatSeconds(seconds);
}
