using System.ComponentModel;

namespace MostaqlK.UI.PlatformComponents.PipelineRadar;

public partial class PipelineRadar : ContentView
{
    private readonly PipelineRadarDrawable _drawable = new();
    private bool _isScanning;
    private bool _isSweeping;
    
    // Animation names to prevent overlap
    private const string DiscoveryScanAnimation = "DiscoveryScan";
    private const string SnapshotSweepAnimation = "SnapshotSweep";
    private const string QueueTransitionAnimation = "QueueTransition";
    private const string WorkerPulseAnimation = "WorkerPulse";

    public static readonly BindableProperty DiscoveryProgressProperty =
        BindableProperty.Create(nameof(DiscoveryProgress), typeof(double), typeof(PipelineRadar), 0.0,
            propertyChanged: (b, o, n) => ((PipelineRadar)b).OnDiscoveryChanged((double)n));

    public double DiscoveryProgress
    {
        get => (double)GetValue(DiscoveryProgressProperty);
        set => SetValue(DiscoveryProgressProperty, value);
    }

    public static readonly BindableProperty QueuePressureProperty =
        BindableProperty.Create(nameof(QueuePressure), typeof(double), typeof(PipelineRadar), 0.0,
            propertyChanged: (b, o, n) => ((PipelineRadar)b).OnQueueChanged((double)n));

    public double QueuePressure
    {
        get => (double)GetValue(QueuePressureProperty);
        set => SetValue(QueuePressureProperty, value);
    }

    public static readonly BindableProperty EnrichmentActivityProperty =
        BindableProperty.Create(nameof(EnrichmentActivity), typeof(double), typeof(PipelineRadar), 0.0,
            propertyChanged: (b, o, n) => ((PipelineRadar)b).OnEnrichmentChanged((double)n));

    public double EnrichmentActivity
    {
        get => (double)GetValue(EnrichmentActivityProperty);
        set => SetValue(EnrichmentActivityProperty, value);
    }

    public static readonly BindableProperty IsSnapshotActiveProperty =
        BindableProperty.Create(nameof(IsSnapshotActive), typeof(bool), typeof(PipelineRadar), false,
            propertyChanged: (b, o, n) => ((PipelineRadar)b).OnSnapshotChanged((bool)n));

    public bool IsSnapshotActive
    {
        get => (bool)GetValue(IsSnapshotActiveProperty);
        set => SetValue(IsSnapshotActiveProperty, value);
    }

    public PipelineRadar()
    {
        InitializeComponent();
        RadarCanvas.Drawable = _drawable;
        
        // Listen for global status changes if needed, or rely on BindableProperties
        // However, for discovery events and detailed worker states, we might need direct subscription
        // if the caller doesn't use the bindable properties.
        // Actually, it's better to provide methods or listen to the service.
    }

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();
        if (Handler != null)
        {
            var status = IPlatformApplication.Current.Services.GetService<GlobalAppStatusService>();
            if (status != null)
            {
                status.ProjectDiscovered += OnProjectDiscovered;
                status.WorkerStateChanged += OnWorkerStateChanged;
            }
        }
    }

    private void OnProjectDiscovered()
    {
        MainThread.BeginInvokeOnMainThread(() => TriggerDiscoveryEvent());
    }

    private void OnWorkerStateChanged(int index, WorkerState state)
    {
        MainThread.BeginInvokeOnMainThread(() => {
            var targetActivity = state == WorkerState.Processing ? 1.0 : 0.0;
            _drawable.WorkerStates[index] = state switch
            {
                WorkerState.Processing => RadarWorkerState.Processing,
                WorkerState.Completed => RadarWorkerState.Completed,
                WorkerState.Error => RadarWorkerState.Error,
                _ => RadarWorkerState.Idle
            };
            AnimateWorker(index, targetActivity);
        });
    }

    public void TriggerDiscoveryEvent()
    {
        // Add a particle at the discovery ring
        var angle = _drawable.DiscoveryScanAngle * Math.PI / 180;
        var r = (Math.Min(RadarCanvas.Width, RadarCanvas.Height) / 2 - 4) * 0.95;
        var cx = RadarCanvas.Width / 2;
        var cy = RadarCanvas.Height / 2;
        
        var particle = new RadarParticle
        {
            X = (float)(cx + Math.Cos(angle) * r),
            Y = (float)(cy + Math.Sin(angle) * r),
            Opacity = 1f,
            Scale = 1f,
            Color = Colors.DeepSkyBlue
        };

        lock (_drawable.Particles)
        {
            _drawable.Particles.Add(particle);
        }

        // Animate particle moving to queue ring
        var targetR = (Math.Min(RadarCanvas.Width, RadarCanvas.Height) / 2 - 4) * 0.75;
        
        new Animation(v => {
            particle.Scale = (float)(1.0 - v * 0.5);
            particle.Opacity = (float)(1.0 - v * 0.5);
            var currentR = r + (targetR - r) * v;
            particle.X = (float)(cx + Math.Cos(angle) * currentR);
            particle.Y = (float)(cy + Math.Sin(angle) * currentR);
            RadarCanvas.Invalidate();
        }, 0, 1)
        .Commit(this, $"DiscoveryParticle_{Guid.NewGuid()}", length: 500, easing: Easing.CubicIn, finished: (v, aborted) => {
            lock (_drawable.Particles)
            {
                _drawable.Particles.Remove(particle);
            }
            RadarCanvas.Invalidate();
        });
    }

    private void OnDiscoveryChanged(double progress)
    {
        if (progress > 0 && !_isScanning)
        {
            StartDiscoveryScan();
        }
        else if (progress <= 0 && _isScanning)
        {
            StopDiscoveryScan();
        }
    }

    private void StartDiscoveryScan()
    {
        _isScanning = true;
        
        // 1. Fade in the ring
        new Animation(v => {
            _drawable.DiscoveryScanActive = v;
            RadarCanvas.Invalidate();
        }, 0, 1).Commit(this, "DiscoveryFadeIn", length: 300);

        // 2. Rotate the segment
        var rotation = new Animation(v => {
            _drawable.DiscoveryScanAngle = v;
            RadarCanvas.Invalidate();
        }, 0, 360);
        
        rotation.Commit(this, DiscoveryScanAnimation, length: 2000, repeat: () => _isScanning);
    }

    private void StopDiscoveryScan()
    {
        _isScanning = false;
        this.AbortAnimation(DiscoveryScanAnimation);
        
        new Animation(v => {
            _drawable.DiscoveryScanActive = v;
            RadarCanvas.Invalidate();
        }, 1, 0).Commit(this, "DiscoveryFadeOut", length: 300);
    }

    private void OnQueueChanged(double newPressure)
    {
        this.AbortAnimation(QueueTransitionAnimation);
        
        new Animation(v => {
            _drawable.QueuePressure = v;
            RadarCanvas.Invalidate();
        }, _drawable.QueuePressure, newPressure)
        .Commit(this, QueueTransitionAnimation, length: 500, easing: Easing.CubicInOut);
    }

    private void OnEnrichmentChanged(double activity)
    {
        // For simplicity, we treat activity as a mask for 3 workers
        // In a real app, we'd map WorkerPool.ActiveCount to specific segments
        int activeCount = (int)Math.Round(activity * 3);
        
        for (int i = 0; i < 3; i++)
        {
            double target = i < activeCount ? 1.0 : 0.0;
            AnimateWorker(i, target);
        }
    }

    public void TriggerTransitionToWorker(int workerIndex)
    {
        if (workerIndex < 0 || workerIndex >= 3) return;

        var cx = RadarCanvas.Width / 2;
        var cy = RadarCanvas.Height / 2;
        var radius = Math.Min(RadarCanvas.Width, RadarCanvas.Height) / 2 - 4;
        
        var startR = radius * 0.75f;
        var endR = radius * 0.5f;
        
        // Pick an angle on the queue ring corresponding to the worker segment
        var gap = 10f;
        var workerAngle = -90f + (workerIndex * 120f) + (120f / 2f);
        var angleRad = workerAngle * Math.PI / 180;

        var particle = new RadarParticle
        {
            X = (float)(cx + Math.Cos(angleRad) * startR),
            Y = (float)(cy + Math.Sin(angleRad) * startR),
            Opacity = 1f,
            Scale = 0.5f,
            Color = Colors.Orange
        };

        lock (_drawable.Particles)
        {
            _drawable.Particles.Add(particle);
        }

        new Animation(v => {
            var currentR = startR + (endR - startR) * v;
            particle.X = (float)(cx + Math.Cos(angleRad) * currentR);
            particle.Y = (float)(cy + Math.Sin(angleRad) * currentR);
            particle.Opacity = (float)(1.0 - v * 0.3);
            particle.Scale = (float)(0.5 + v * 0.5);
            RadarCanvas.Invalidate();
        }, 0, 1)
        .Commit(this, $"TransitionParticle_{Guid.NewGuid()}", length: 600, easing: Easing.CubicOut, finished: (v, aborted) => {
            lock (_drawable.Particles)
            {
                _drawable.Particles.Remove(particle);
            }
            RadarCanvas.Invalidate();
        });
    }

    private void AnimateWorker(int index, double target)
    {
        string name = $"Worker_{index}";
        bool wasActive = _drawable.WorkerActivity[index] > 0.5;
        bool isNowActive = target > 0.5;

        if (isNowActive && !wasActive)
        {
            TriggerTransitionToWorker(index);
        }

        this.AbortAnimation(name);

        new Animation(v => {
            _drawable.WorkerActivity[index] = v;
            RadarCanvas.Invalidate();
        }, _drawable.WorkerActivity[index], target)
        .Commit(this, name, length: 400, easing: Easing.SinOut, finished: (v, aborted) => {
            if (!aborted && wasActive && !isNowActive)
            {
                TriggerCompletionPulse(index);
            }
        });
    }

    public void TriggerCompletionPulse(int index)
    {
        if (index < 0 || index >= 3) return;
        
        string name = $"WorkerPulse_{index}";
        this.AbortAnimation(name);

        new Animation(v => {
            _drawable.WorkerPulse[index] = v;
            RadarCanvas.Invalidate();
        }, 0, 1)
        .Commit(this, name, length: 600, easing: Easing.SinOut);
    }

    private void OnSnapshotChanged(bool active)
    {
        if (active && !_isSweeping)
        {
            StartSnapshotSweep();
        }
        else if (!active)
        {
            _isSweeping = false;
        }
    }

    private void OnPointerEntered(object sender, PointerEventArgs e)
    {
        _drawable.IsHovered = true;
        RadarCanvas.Invalidate();
        
        // Spec: Fast hover effects (100-180ms)
        this.AbortAnimation("HoverAnim");
        new Animation(v => {
            // We could animate a specific scale or glow here if desired
            RadarCanvas.Scale = v;
        }, RadarCanvas.Scale, 1.1)
        .Commit(this, "HoverAnim", length: 150, easing: Easing.CubicOut);
    }

    private void OnPointerExited(object sender, PointerEventArgs e)
    {
        _drawable.IsHovered = false;
        RadarCanvas.Invalidate();
        
        this.AbortAnimation("HoverAnim");
        new Animation(v => {
            RadarCanvas.Scale = v;
        }, RadarCanvas.Scale, 1.0)
        .Commit(this, "HoverAnim", length: 150, easing: Easing.CubicIn);
    }

    private void StartSnapshotSweep()
    {
        _isSweeping = true;
        _drawable.IsSnapshotActive = true;
        
        new Animation(v => {
            _drawable.SweepAngle = (float)v;
            RadarCanvas.Invalidate();
        }, 0, 360)
        .Commit(this, SnapshotSweepAnimation, length: 1500, repeat: () => _isSweeping, finished: (v, aborted) => {
            if (!aborted && !_isSweeping)
            {
                _drawable.IsSnapshotActive = false;
                RadarCanvas.Invalidate();
            }
        });
    }
}
