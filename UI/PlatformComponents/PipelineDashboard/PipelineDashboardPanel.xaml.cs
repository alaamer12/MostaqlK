using System.Globalization;
using MostaqlK.Core.Formatting;
using MostaqlK.Services;
using MostaqlK.Services.Diagnostics;
using MostaqlK.UI.PlatformComponents.PipelineRadar;

namespace MostaqlK.UI.PlatformComponents.PipelineDashboard;

/// <summary>
/// The pipeline dashboard column: the Lighthouse Radar at a readable size, followed by the
/// discovery/queue summary cards, the three worker rows and a drill-in block that follows the
/// dial's selection. It replaces the old 56px footer dial, which was too small to read and faded
/// to nothing whenever the pipeline went idle.
/// <para>
/// Layout is a header / scrollable body pair with a collapsed rail sharing the same cell, so
/// collapsing only animates the panel's width and swaps visibility - the pipeline never disappears
/// (the rail keeps the worker dots and backlog utilisation on screen) and no layout is rebuilt.
/// Text is refreshed from one dispatcher timer; all *motion* still belongs to the radar's single
/// ticker, and the figures it exposes are already interpolated, so numbers glide instead of
/// snapping.
/// </para>
/// </summary>
public partial class PipelineDashboardPanel : ContentView
{
    private const string KeyIsExpanded = "pipeline_panel_is_expanded";
    private const string KeyWidth = "pipeline_panel_width";

    private const string WidthAnimationName = "PipelinePanelWidth";
    private const double ToggleDuration = 240;
    private const double TextRefreshMilliseconds = 250;

    /// <summary>Width of the collapsed status rail. Also the panel's minimum footprint.</summary>
    public const double RailWidth = 40;

    private readonly IDispatcherTimer? _textTimer;
    private readonly BoxView[] _workerDots;
    private readonly BoxView[] _railDots;
    private readonly Border[] _workerCards;
    private readonly Label[] _workerProjectLabels;
    private readonly Label[] _workerTimeLabels;

    private GlobalAppStatusService? _status;
    private RadarRegion _hoveredRegion = RadarRegion.None;
    private int _hoveredWorker = -1;
    private double _queueBarWidth;
    private bool _isSliding;

    public PipelineDashboardPanel()
    {
        InitializeComponent();

        _workerDots = [Worker0Dot, Worker1Dot, Worker2Dot];
        _railDots = [RailDot0, RailDot1, RailDot2];
        _workerCards = [Worker0Card, Worker1Card, Worker2Card];
        _workerProjectLabels = [Worker0ProjectLabel, Worker1ProjectLabel, Worker2ProjectLabel];
        _workerTimeLabels = [Worker0TimeLabel, Worker1TimeLabel, Worker2TimeLabel];

        // "Open on first run, then remembered" - the stored width is clamped by the splitter, so a
        // value saved on a wider monitor can never wedge the feed off-screen.
        ExpandedWidth = Microsoft.Maui.Storage.Preferences.Get(KeyWidth, ExpandedWidth);
        IsExpanded = Microsoft.Maui.Storage.Preferences.Get(KeyIsExpanded, true);

        Radar.HoverChanged += OnRadarHoverChanged;
        Radar.FocusedWorkerChanged += OnRadarFocusedWorkerChanged;
        // Clicking the discovery or queue ring pins that ring's drill-in; without this the click
        // had no observable effect at all, because the section only ever followed the pointer.
        Radar.SelectionChanged += OnRadarSelectionChanged;

        // Only the *settled* width drives the dial size: resizing a GraphicsView on every frame of
        // the collapse slide forced a measure pass per frame, which is what made the slide hop.
        SizeChanged += (_, _) =>
        {
            if (!_isSliding)
            {
                ApplyRadarDiameter();
            }
        };

        _textTimer = Dispatcher.CreateTimer();
        if (_textTimer is not null)
        {
            _textTimer.Interval = TimeSpan.FromMilliseconds(TextRefreshMilliseconds);
            _textTimer.Tick += (_, _) => RefreshReadout();
        }

        ApplyExpandedState(animate: false);
        RefreshReadout();
    }

    // ------------------------------------------------------------------ bindable pipeline inputs

    public static readonly BindableProperty DiscoveryProgressProperty = BindableProperty.Create(
        nameof(DiscoveryProgress), typeof(double), typeof(PipelineDashboardPanel), 0.0);

    /// <summary>Mirrors <see cref="PipelineRadar.PipelineRadar.DiscoveryProgress"/>.</summary>
    public double DiscoveryProgress
    {
        get => (double)GetValue(DiscoveryProgressProperty);
        set => SetValue(DiscoveryProgressProperty, value);
    }

    public static readonly BindableProperty QueuePressureProperty = BindableProperty.Create(
        nameof(QueuePressure), typeof(double), typeof(PipelineDashboardPanel), 0.0);

    /// <summary>Backlog utilisation (0..1).</summary>
    public double QueuePressure
    {
        get => (double)GetValue(QueuePressureProperty);
        set => SetValue(QueuePressureProperty, value);
    }

    public static readonly BindableProperty QueueCountProperty = BindableProperty.Create(
        nameof(QueueCount), typeof(int), typeof(PipelineDashboardPanel), 0);

    /// <summary>Number of items waiting in the backlog.</summary>
    public int QueueCount
    {
        get => (int)GetValue(QueueCountProperty);
        set => SetValue(QueueCountProperty, value);
    }

    public static readonly BindableProperty QueueCapacityProperty = BindableProperty.Create(
        nameof(QueueCapacity), typeof(int), typeof(PipelineDashboardPanel), 50);

    /// <summary>Backlog capacity the utilisation percentage is measured against.</summary>
    public int QueueCapacity
    {
        get => (int)GetValue(QueueCapacityProperty);
        set => SetValue(QueueCapacityProperty, value);
    }

    public static readonly BindableProperty EnrichmentActivityProperty = BindableProperty.Create(
        nameof(EnrichmentActivity), typeof(double), typeof(PipelineDashboardPanel), 0.0);

    /// <summary>Coarse share of busy workers, used when per-worker state is unavailable.</summary>
    public double EnrichmentActivity
    {
        get => (double)GetValue(EnrichmentActivityProperty);
        set => SetValue(EnrichmentActivityProperty, value);
    }

    public static readonly BindableProperty IsSnapshotActiveProperty = BindableProperty.Create(
        nameof(IsSnapshotActive), typeof(bool), typeof(PipelineDashboardPanel), false);

    /// <summary>True while the diff engine holds a point-in-time snapshot.</summary>
    public bool IsSnapshotActive
    {
        get => (bool)GetValue(IsSnapshotActiveProperty);
        set => SetValue(IsSnapshotActiveProperty, value);
    }

    // ------------------------------------------------------------------ panel geometry

    public static readonly BindableProperty IsExpandedProperty = BindableProperty.Create(
        nameof(IsExpanded), typeof(bool), typeof(PipelineDashboardPanel), true,
        defaultBindingMode: BindingMode.TwoWay,
        propertyChanged: OnIsExpandedChanged);

    /// <summary>Whether the full panel is shown, or only the collapsed status rail.</summary>
    public bool IsExpanded
    {
        get => (bool)GetValue(IsExpandedProperty);
        set => SetValue(IsExpandedProperty, value);
    }

    public static readonly BindableProperty ExpandedWidthProperty = BindableProperty.Create(
        nameof(ExpandedWidth), typeof(double), typeof(PipelineDashboardPanel), 320.0,
        defaultBindingMode: BindingMode.TwoWay,
        propertyChanged: OnExpandedWidthChanged);

    /// <summary>
    /// Width of the expanded panel. Two-way bindable so the drag handle
    /// (<see cref="PlatformComponents.SplitterHandle"/>) can drive it live while panning.
    /// </summary>
    public double ExpandedWidth
    {
        get => (double)GetValue(ExpandedWidthProperty);
        set => SetValue(ExpandedWidthProperty, value);
    }

    private static void OnIsExpandedChanged(BindableObject bindable, object oldValue, object newValue) =>
        ((PipelineDashboardPanel)bindable).ApplyExpandedState(animate: true);

    private static void OnExpandedWidthChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var panel = (PipelineDashboardPanel)bindable;
        if (panel.IsExpanded && !panel._isSliding)
        {
            panel.WidthRequest = (double)newValue;
            panel.ApplyRadarDiameter();
        }
    }

    /// <summary>Persists the current width; called by the host when a drag finishes.</summary>
    public void PersistWidth() => Microsoft.Maui.Storage.Preferences.Set(KeyWidth, ExpandedWidth);

    private void ApplyExpandedState(bool animate)
    {
        var target = IsExpanded ? ExpandedWidth : RailWidth;

        CollapseGlyph.Text = IsExpanded ? "»" : "«";

        // Rapid toggling must never queue two width animations against each other.
        this.AbortAnimation(WidthAnimationName);

        if (!animate || MotionPreferences.IsReducedMotionRequested || Width <= 0)
        {
            // Reduced motion still changes state - it just does not travel.
            EndSlide();
            WidthRequest = target;
            Body.IsVisible = IsExpanded;
            Rail.IsVisible = !IsExpanded;
            Body.Opacity = IsExpanded ? 1 : 0;
            Rail.Opacity = IsExpanded ? 0 : 1;
            ApplyRadarDiameter();
            return;
        }

        // Both halves stay laid out for the length of the slide so the outgoing one can fade out
        // instead of blinking away; the loser is hidden once the animation settles.
        Body.IsVisible = true;
        Rail.IsVisible = true;

        BeginSlide();

        // One animation, not three: the width and both fades share a single tick, so every frame
        // performs exactly one layout pass. Two extra parallel animations against the same subtree
        // were interleaving their own invalidations, which read as stutter.
        var from = Width;
        var bodyFrom = Body.Opacity;
        var railFrom = Rail.Opacity;
        var bodyTo = IsExpanded ? 1 : 0;
        var railTo = IsExpanded ? 0 : 1;

        new Animation
        {
            { 0, 1, new Animation(v => WidthRequest = v, from, target, Easing.CubicOut) },
            { 0, 1, new Animation(v => Body.Opacity = v, bodyFrom, bodyTo, Easing.CubicOut) },
            { 0, 1, new Animation(v => Rail.Opacity = v, railFrom, railTo, Easing.CubicOut) }
        }.Commit(this, WidthAnimationName, length: (uint)ToggleDuration, finished: (_, _) =>
        {
            Body.IsVisible = IsExpanded;
            Rail.IsVisible = !IsExpanded;
            EndSlide();
            ApplyRadarDiameter();
        });
    }

    /// <summary>
    /// Quiets everything that would otherwise invalidate this subtree while the width animates: the
    /// radar's frame ticker, its canvas resizing, and the readout timer. The pipeline keeps running
    /// on its own threads - only the *rendering* of it pauses, and the state it advanced is picked
    /// up in full the moment the slide ends.
    /// </summary>
    private void BeginSlide()
    {
        if (_isSliding)
        {
            return;
        }

        _isSliding = true;
        _textTimer?.Stop();
        Radar.FreezeRendering();
    }

    private void EndSlide()
    {
        if (!_isSliding)
        {
            return;
        }

        _isSliding = false;
        Radar.ResumeRendering();

        if (Handler is not null)
        {
            _textTimer?.Start();
            RefreshReadout();
        }
    }

    private void ApplyRadarDiameter()
    {
        if (!IsExpanded)
        {
            return;
        }

        // The dial takes the panel width minus its padding, capped so it stays a dial and not a
        // wall - and floored so it never shrinks back to the unreadable footer size.
        // Quantised to 8dp steps: while the user pans the splitter the width changes on every
        // pointer move, and re-sizing the canvas each time relayouts the whole column for a change
        // nobody can see. Snapping means the dial resizes a handful of times per drag instead.
        var diameter = Math.Clamp(ExpandedWidth - 48, 140, 260);
        Radar.Diameter = Math.Round(diameter / 8.0) * 8.0;
    }

    private void OnQueueBarSizeChanged(object? sender, EventArgs e)
    {
        _queueBarWidth = QueueBar.Width;
        ApplyQueueBar(Radar.DisplayedUtilisation);
    }

    private void ApplyQueueBar(double utilisation)
    {
        if (_queueBarWidth <= 0)
        {
            return;
        }

        QueueBarFill.WidthRequest = Math.Clamp(utilisation, 0, 1) * _queueBarWidth;
    }

    // ------------------------------------------------------------------ service wiring

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();

        if (Handler is null)
        {
            _textTimer?.Stop();
            return;
        }

        _status ??= IPlatformApplication.Current?.Services.GetService<GlobalAppStatusService>();
        _textTimer?.Start();
        ApplyRadarDiameter();
        RefreshReadout();
    }

    // ------------------------------------------------------------------ selection

    private void OnRadarHoverChanged(RadarRegion region, int workerIndex)
    {
        _hoveredRegion = region;
        _hoveredWorker = workerIndex;
        RefreshReadout();
    }

    private void OnRadarSelectionChanged(RadarRegion region, int workerIndex) => RefreshReadout();

    private void OnRadarFocusedWorkerChanged(int workerIndex)
    {
        ApplyFocusEmphasis(workerIndex);
        RefreshReadout();
    }

    /// <summary>
    /// Focus mode: the selected worker's row stays fully lit while the others quieten, mirroring
    /// what the dial does to its segments, so the panel and the chart read as one selection.
    /// </summary>
    private void ApplyFocusEmphasis(int focusedWorker)
    {
        for (var i = 0; i < _workerCards.Length; i++)
        {
            var isFocused = focusedWorker < 0 || focusedWorker == i;
            _workerCards[i].Opacity = isFocused ? 1 : 0.55;
        }
    }

    [TraceInteraction("Pipeline_PanelToggle")]
    [MostaqlK.Core.ErrorOutcome(MostaqlK.Core.ErrorOutcome.Rethrown, Label = "Pipeline_PanelToggle")]
    private void OnToggleClicked(object? sender, EventArgs e)
    {
        using var _ = TraceScope.Begin("Pipeline_PanelToggle");
        try
        {
            IsExpanded = !IsExpanded;
            Microsoft.Maui.Storage.Preferences.Set(KeyIsExpanded, IsExpanded);
        }
        catch (Exception ex)
        {
            _.MarkFaulted(ex);
            throw;
        }
    }

    private void OnWorker0Clicked(object? sender, EventArgs e) => FocusWorker(0);

    private void OnWorker1Clicked(object? sender, EventArgs e) => FocusWorker(1);

    private void OnWorker2Clicked(object? sender, EventArgs e) => FocusWorker(2);

    [TraceInteraction("Pipeline_WorkerCard")]
    [MostaqlK.Core.ErrorOutcome(MostaqlK.Core.ErrorOutcome.Rethrown, Label = "Pipeline_WorkerCard")]
    private void FocusWorker(int index)
    {
        using var _ = TraceScope.Begin("Pipeline_WorkerCard", index.ToString(CultureInfo.InvariantCulture));
        try
        {
            // Clicking the already-focused row releases focus, same as clicking its dial segment.
            Radar.FocusWorker(Radar.FocusedWorker == index ? -1 : index);
        }
        catch (Exception ex)
        {
            _.MarkFaulted(ex);
            throw;
        }
    }

    // ------------------------------------------------------------------ readout

    private void RefreshReadout()
    {
        var capacity = Math.Max(1, _status?.QueueCapacity > 0 ? _status.QueueCapacity : QueueCapacity);
        var utilisation = Radar.DisplayedUtilisation;
        var queueCount = Radar.DisplayedQueueCount;

        if (IsExpanded)
        {
            RefreshDiscovery();
            RefreshQueue(queueCount, capacity, utilisation);
            RefreshWorkers();
            RefreshDetail(capacity, queueCount, utilisation);
        }

        RefreshRail(utilisation);
    }

    private void RefreshDiscovery()
    {
        var scanning = Radar.IsScanning;
        DiscoveryDot.Color = scanning
            ? (IsDark ? Color.FromArgb("#5CA8DE") : Color.FromArgb("#1D6FA5"))
            : IdleDotColor;

        Assign(DiscoveryStatusLabel, scanning ? "الحالة: جارٍ الفحص" : "الحالة: خامل");

        // The shared readout words and re-times itself; the panel only hands it the timestamp.
        DiscoveryLastScan.LastScanAt = _status?.LastScanCompletedAt;
        Assign(DiscoveryCountLabel, $"مشاريع مكتشفة: {_status?.ProjectsDiscoveredCount ?? 0}");
        Assign(DiscoveryIntervalLabel, $"فترة الفحص: {_status?.ScanIntervalSeconds ?? 0} ث");

        // Outcome of the last *attempt*. Before this, a scan that failed and a scan that succeeded
        // with nothing new both left the whole panel at zero, so neither could be told apart from a
        // pipeline that was not running at all.
        var attempts = _status?.ScanAttemptCount ?? 0;
        Assign(DiscoveryResultLabel, attempts == 0
            ? "محاولات الفحص: 0"
            : $"آخر نتيجة: {_status?.LastScanSeenCount ?? 0} مشروع، {_status?.LastScanNewCount ?? 0} جديد (محاولات: {attempts})");

        var failed = _status?.LastScanFailed == true;
        var errorText = failed
            ? $"فشل الفحص: {_status?.LastScanErrorMessage}"
            : string.Empty;
        var fixText = failed ? _status?.LastScanFixMessage ?? string.Empty : string.Empty;

        // Assign already collapses a label whose text is empty, so a healthy pipeline shows no
        // error rows at all.
        Assign(DiscoveryErrorLabel, errorText);
        Assign(DiscoveryFixLabel, fixText);
    }

    private void RefreshQueue(double queueCount, int capacity, double utilisation)
    {
        Assign(QueueValueLabel, $"{Math.Round(queueCount)} / {capacity}");
        Assign(QueueUtilLabel, $"{Math.Round(utilisation * 100)}%");
        Assign(QueueOldestLabel, $"أقدم عنصر: {Format(_status?.OldestQueuedItemSeconds ?? 0)} ث");
        Assign(QueueAvgLabel, $"متوسط الانتظار: {Format(_status?.AverageQueueWaitSeconds ?? 0)} ث");
        ApplyQueueBar(utilisation);
    }

    private void RefreshWorkers()
    {
        for (var i = 0; i < _workerDots.Length; i++)
        {
            var state = Radar.WorkerStateAt(i);
            _workerDots[i].Color = ColorFor(state);

            var telemetry = _status?.Workers[i];
            var project = !string.IsNullOrEmpty(telemetry?.CurrentProjectTitle)
                ? telemetry!.CurrentProjectTitle
                : Radar.ProjectTitleOfWorker(i) is { Length: > 0 } title
                    ? title
                    : StatusText(state);

            Assign(_workerProjectLabels[i], project);
            // The row is one truncated line wide, so the full project name lives in a native
            // tooltip - a real title is far longer than the `#id` this used to show.
            ToolTipProperties.SetText(_workerProjectLabels[i], project);
            Assign(_workerTimeLabels[i], state == RadarWorkerState.Processing
                ? $"{Format(telemetry?.ElapsedSeconds ?? 0)} ث"
                : $"{telemetry?.CompletedCount ?? 0} ✓");
        }
    }

    private void RefreshRail(double utilisation)
    {
        for (var i = 0; i < _railDots.Length; i++)
        {
            _railDots[i].Color = ColorFor(Radar.WorkerStateAt(i));
        }

        Assign(RailQueueLabel, $"{Math.Round(utilisation * 100)}%");
    }

    /// <summary>
    /// The drill-in block: an explicit selection (a clicked worker) always wins, then whatever the
    /// pointer is over, and with neither it falls back to the backlog overview - so the section is
    /// never blank.
    /// </summary>
    private void RefreshDetail(int capacity, double queueCount, double utilisation)
    {
        var worker = Radar.FocusedWorker >= 0
            ? Radar.FocusedWorker
            : _hoveredRegion == RadarRegion.Worker ? _hoveredWorker : -1;

        // The pointer wins while it is actually over a ring, but a *clicked* ring stays pinned once
        // the pointer leaves - otherwise a click on the queue ring was indistinguishable from a
        // hover that ended, which is exactly why it looked like nothing happened.
        var pinned = Radar.SelectedRegion;
        var region = _hoveredRegion != RadarRegion.None ? _hoveredRegion : pinned;
        var pinnedSuffix = pinned != RadarRegion.None && pinned == region ? " • مثبّت" : string.Empty;

        if (worker >= 0)
        {
            var telemetry = _status?.Workers[worker];
            var state = Radar.WorkerStateAt(worker);
            var project = !string.IsNullOrEmpty(telemetry?.CurrentProjectTitle)
                ? telemetry!.CurrentProjectTitle
                : Radar.ProjectTitleOfWorker(worker) ?? "-";

            SetDetail(
                $"العامل {worker + 1}",
                $"الحالة: {StatusText(state)}",
                $"المشروع الحالي: {project}",
                $"مدة المعالجة: {Format(telemetry?.ElapsedSeconds ?? 0)} ث",
                $"آخر مدة معالجة: {Format(telemetry?.LastProcessingSeconds ?? 0)} ث",
                $"مكتملة: {telemetry?.CompletedCount ?? 0}   أخطاء: {telemetry?.ErrorCount ?? 0}",
                $"نسبة النجاح: {Math.Round((telemetry?.SuccessRate ?? 1) * 100)}%");
            return;
        }

        if (region == RadarRegion.Discovery)
        {
            SetDetail(
                $"الاستكشاف{pinnedSuffix}",
                Radar.IsScanning ? "الحالة: جارٍ الفحص" : "الحالة: خامل",
                LastScanText.Labelled(_status?.LastScanCompletedAt),
                $"مشاريع مكتشفة: {_status?.ProjectsDiscoveredCount ?? 0}",
                $"فترة الفحص: {_status?.ScanIntervalSeconds ?? 0} ث",
                _status?.ScanAttemptCount is > 0
                    ? $"آخر نتيجة: {_status.LastScanSeenCount} مشروع، {_status.LastScanNewCount} جديد"
                    : string.Empty,
                _status?.LastScanFailed == true
                    ? $"فشل الفحص [{_status.LastScanErrorCode}]: {_status.LastScanErrorMessage}"
                    : string.Empty);
            return;
        }

        // Backlog view: also names the projects the ring is actually made of, so clicking the arc
        // answers "what is waiting?" and not just "how full is it?".
        SetDetail(
            $"قائمة الانتظار{pinnedSuffix}",
            $"{Math.Round(queueCount)} / {capacity}",
            $"{Math.Round(utilisation * 100)}% استخدام",
            $"أقدم عنصر: {Format(_status?.OldestQueuedItemSeconds ?? 0)} ث",
            $"متوسط الانتظار: {Format(_status?.AverageQueueWaitSeconds ?? 0)} ث",
            QueuedTitlesText(),
            region == RadarRegion.Queue ? "انقر الحلقة مرة أخرى لإلغاء التثبيت" : "اختر عاملاً أو حلقة لعرض تفاصيلها");
    }

    /// <summary>
    /// The first few queued project titles, capped so the drill-in cannot grow without bound while
    /// a large backlog drains.
    /// </summary>
    private string QueuedTitlesText()
    {
        var titles = Radar.QueuedProjectTitles.Take(3).ToList();
        if (titles.Count == 0)
        {
            return Radar.VisibleQueuedTokens > 0
                ? $"عناصر في الانتظار: {Radar.VisibleQueuedTokens}"
                : string.Empty;
        }

        var remaining = Radar.VisibleQueuedTokens - titles.Count;
        var joined = string.Join(" • ", titles);
        return remaining > 0 ? $"في الانتظار: {joined} (+{remaining})" : $"في الانتظار: {joined}";
    }

    private void SetDetail(string title, string line1, string line2, string line3, string line4, string line5, string line6)
    {
        Assign(DetailTitleLabel, title);
        Assign(DetailLine1, line1);
        Assign(DetailLine2, line2);
        Assign(DetailLine3, line3);
        Assign(DetailLine4, line4);
        Assign(DetailLine5, line5);
        Assign(DetailLine6, line6);
    }

    // Only assign when the text actually changed: this runs on a timer, and a redundant assignment
    // costs a layout pass.
    private static void Assign(Label label, string text)
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

    private static bool IsDark => (Application.Current?.RequestedTheme ?? AppTheme.Dark) == AppTheme.Dark;

    /// <summary>
    /// Idle indicators need a colour that reads on *both* surfaces: the old single slate value
    /// disappeared into the dark panel, which is what made the resting state look empty.
    /// </summary>
    private static Color IdleDotColor => IsDark ? Color.FromArgb("#7C8CA5") : Color.FromArgb("#64748B");

    private static Color ColorFor(RadarWorkerState state) => state switch
    {
        RadarWorkerState.Processing => IsDark ? Color.FromArgb("#22C55E") : Color.FromArgb("#15803D"),
        RadarWorkerState.Completed => IsDark ? Color.FromArgb("#38BDF8") : Color.FromArgb("#0369A1"),
        RadarWorkerState.Error => IsDark ? Color.FromArgb("#EF4444") : Color.FromArgb("#B91C1C"),
        _ => IdleDotColor
    };

    private static string StatusText(RadarWorkerState state) => state switch
    {
        RadarWorkerState.Processing => "يعالج",
        RadarWorkerState.Completed => "مكتمل",
        RadarWorkerState.Error => "خطأ",
        _ => "خامل"
    };

    private static string Format(double seconds) =>
        seconds.ToString(seconds >= 100 ? "0" : "0.0", CultureInfo.InvariantCulture);
}
