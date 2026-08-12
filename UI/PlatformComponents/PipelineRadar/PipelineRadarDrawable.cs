using Microsoft.Maui.Graphics;

namespace MostaqlK.UI.PlatformComponents.PipelineRadar;

public enum RadarWorkerState
{
    Idle,
    Processing,
    Completed,
    Error
}

public class RadarParticle
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Opacity { get; set; }
    public float Scale { get; set; }
    public Color Color { get; set; }
}

public class PipelineRadarDrawable : IDrawable
{
    // --- State-driven values (Interpolated by Animation logic) ---
    public double DiscoveryScanAngle { get; set; }
    public double DiscoveryScanActive { get; set; } // 0 to 1 opacity
    
    public double QueuePressure { get; set; } // 0 to 1
    
    // Workers: Array for 3 workers
    public double[] WorkerActivity { get; } = new double[3]; // 0 to 1 (brightness/glow)
    public double[] WorkerPulse { get; } = new double[3]; // 0 to 1 (completion pulse expansion)
    public RadarWorkerState[] WorkerStates { get; } = new RadarWorkerState[3];

    public List<RadarParticle> Particles { get; } = new();
    
    public float SweepAngle { get; set; }
    public bool IsSnapshotActive { get; set; }
    
    public bool IsHovered { get; set; }

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        float centerX = dirtyRect.Center.X;
        float centerY = dirtyRect.Center.Y;
        float radius = Math.Min(dirtyRect.Width, dirtyRect.Height) / 2 - 4; // Padding

        canvas.Antialias = true;

        // 1. Draw Outer Discovery Ring (Dashed)
        DrawDiscoveryRing(canvas, centerX, centerY, radius * 0.95f);

        // 2. Draw Middle Queue Ring (Backlog)
        DrawQueueRing(canvas, centerX, centerY, radius * 0.75f);

        // 3. Draw Inner Worker Ring (3 segments)
        DrawWorkerRing(canvas, centerX, centerY, radius * 0.5f);

        // 4. Draw Perpendicular Snapshot Sweep (Radar)
        if (IsSnapshotActive)
        {
            DrawSnapshotSweep(canvas, centerX, centerY, radius);
        }

        // 5. Draw Particles
        DrawParticles(canvas);
        
        // 6. Interaction highlight
        if (IsHovered)
        {
            canvas.StrokeColor = Colors.White.WithAlpha(0.05f);
            canvas.StrokeSize = 1f;
            canvas.DrawCircle(centerX, centerY, radius + 2);
        }
    }

    private void DrawParticles(ICanvas canvas)
    {
        canvas.SaveState();
        foreach (var p in Particles)
        {
            canvas.FillColor = p.Color.WithAlpha(p.Opacity);
            float s = p.Scale * 4f;
            canvas.FillEllipse(p.X - s/2, p.Y - s/2, s, s);
        }
        canvas.RestoreState();
    }

    private void DrawDiscoveryRing(ICanvas canvas, float cx, float cy, float r)
    {
        canvas.SaveState();
        
        // Static dashed ring base
        canvas.StrokeColor = Colors.DeepSkyBlue.WithAlpha(0.1f);
        canvas.StrokeSize = 1.5f;
        canvas.StrokeDashPattern = new float[] { 4, 4 };
        canvas.DrawCircle(cx, cy, r);

        // Active Scan Segment
        if (DiscoveryScanActive > 0)
        {
            canvas.StrokeColor = Colors.DeepSkyBlue.WithAlpha((float)DiscoveryScanActive);
            canvas.StrokeSize = 2.5f;
            canvas.StrokeDashPattern = null; // Solid segment
            
            // Draw a trailing segment (e.g. 45 degrees)
            float startAngle = (float)DiscoveryScanAngle - 45;
            canvas.DrawArc(cx - r, cy - r, r * 2, r * 2, startAngle, (float)DiscoveryScanAngle, true, false);
            
            // Detection Pulse (Example: if we had a detection event, we'd draw it here)
        }
        
        canvas.RestoreState();
    }

    private void DrawQueueRing(ICanvas canvas, float cx, float cy, float r)
    {
        canvas.SaveState();
        
        // Background track
        canvas.StrokeColor = Colors.Orange.WithAlpha(0.05f);
        canvas.StrokeSize = 4f;
        canvas.DrawCircle(cx, cy, r);

        // Utilization arc
        if (QueuePressure > 0.01)
        {
            canvas.StrokeColor = Colors.Orange.WithAlpha(0.8f);
            // Draw arc from top (-90 degrees)
            canvas.DrawArc(cx - r, cy - r, r * 2, r * 2, -90, (float)(-90 + (QueuePressure * 360)), true, false);
            
            // Draw small "ticks" or dots representing items if pressure is low enough to see them
            // or just a glow at the end of the arc
            float endAngle = (float)(-90 + (QueuePressure * 360));
            float ex = cx + (float)Math.Cos(endAngle * Math.PI / 180) * r;
            float ey = cy + (float)Math.Sin(endAngle * Math.PI / 180) * r;
            canvas.FillColor = Colors.Orange;
            canvas.FillCircle(ex, ey, 2f);
        }
        
        canvas.RestoreState();
    }

    private void DrawWorkerRing(ICanvas canvas, float cx, float cy, float r)
    {
        canvas.SaveState();
        
        float gap = 10f; // degrees
        float sweep = (360f / 3f) - gap;

        for (int i = 0; i < 3; i++)
        {
            float startAngle = -90f + (i * 120f) + (gap / 2f);
            
            // Worker segment base
            canvas.StrokeColor = Colors.LimeGreen.WithAlpha(0.1f);
            canvas.StrokeSize = 6f;
            
            // Adjust color based on state
            Color stateColor = WorkerStates[i] switch
            {
                RadarWorkerState.Processing => Colors.LimeGreen,
                RadarWorkerState.Completed => Colors.DeepSkyBlue,
                RadarWorkerState.Error => Colors.Red,
                _ => Colors.LimeGreen.WithAlpha(0.2f)
            };

            canvas.StrokeColor = stateColor.WithAlpha(0.1f);
            canvas.DrawArc(cx - r, cy - r, r * 2, r * 2, startAngle, startAngle + sweep, true, false);

            // Active State
            if (WorkerActivity[i] > 0)
            {
                // Glow effect
                canvas.StrokeColor = stateColor.WithAlpha((float)(0.4f + (WorkerActivity[i] * 0.6f)));
                canvas.StrokeSize = 6f + (float)(WorkerActivity[i] * 2f);
                canvas.DrawArc(cx - r, cy - r, r * 2, r * 2, startAngle, startAngle + sweep, true, false);
            }
            
            // Completion Pulse
            if (WorkerPulse[i] > 0)
            {
                float pulseR = r + (float)(WorkerPulse[i] * 20f);
                canvas.StrokeColor = stateColor.WithAlpha((float)(1.0f - WorkerPulse[i]));
                canvas.StrokeSize = 1f;
                canvas.DrawArc(cx - pulseR, cy - pulseR, pulseR * 2, pulseR * 2, startAngle, startAngle + sweep, true, false);
            }
        }
        
        canvas.RestoreState();
    }

    private void DrawSnapshotSweep(ICanvas canvas, float cx, float cy, float r)
    {
        canvas.SaveState();
        
        // Radial needle with gradient trail
        canvas.StrokeSize = 2f;
        
        // Draw the tail (30 degree fade)
        for (int i = 0; i < 30; i++)
        {
            float angle = SweepAngle - i;
            float alpha = (1.0f - (i / 30.0f)) * 0.3f;
            canvas.StrokeColor = Colors.White.WithAlpha(alpha);
            
            float x2 = cx + (float)Math.Cos(angle * Math.PI / 180) * r;
            float y2 = cy + (float)Math.Sin(angle * Math.PI / 180) * r;
            canvas.DrawLine(cx, cy, x2, y2);
        }
        
        // The sharp needle head
        canvas.StrokeColor = Colors.White;
        float nx = cx + (float)Math.Cos(SweepAngle * Math.PI / 180) * r;
        float ny = cy + (float)Math.Sin(SweepAngle * Math.PI / 180) * r;
        canvas.DrawLine(cx, cy, nx, ny);

        canvas.RestoreState();
    }
}
