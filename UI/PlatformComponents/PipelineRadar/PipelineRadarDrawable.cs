using Microsoft.Maui.Graphics;

namespace MostaqlK.UI.PlatformComponents.PipelineRadar;

/// <summary>
/// Pure rendering layer for the Lighthouse Radar. It owns no timers and no animation logic: it only
/// paints the current <see cref="RadarPipelineState"/>. Everything it needs is a plain field read,
/// and nothing is allocated per frame (colours, dash patterns and geometry buffers are cached), so
/// the draw loop stays cheap even during rapid pipeline updates.
/// </summary>
public sealed class PipelineRadarDrawable : IDrawable
{
    // Two palettes, because one is never legible on both surfaces: the dark-theme hues are the
    // light, luminous ones that read on #0F172A, while light theme needs the darker, saturated
    // variants of the same tokens to survive on a white panel.
    private static readonly Color DiscoveryDark = Color.FromArgb("#5CA8DE");
    private static readonly Color QueueDark = Color.FromArgb("#F59E0B");
    private static readonly Color WorkerDark = Color.FromArgb("#22C55E");
    private static readonly Color CompletedDark = Color.FromArgb("#38BDF8");
    private static readonly Color ErrorDark = Color.FromArgb("#EF4444");

    private static readonly Color DiscoveryLight = Color.FromArgb("#1D6FA5");
    private static readonly Color QueueLight = Color.FromArgb("#B45309");
    private static readonly Color WorkerLight = Color.FromArgb("#15803D");
    private static readonly Color CompletedLight = Color.FromArgb("#0369A1");
    private static readonly Color ErrorLight = Color.FromArgb("#B91C1C");

    // Baseline (idle) alphas used to sit around 0.1, which made the three rings effectively
    // invisible whenever the pipeline was quiet - the whole point of the dial is that its resting
    // state is still readable, so the baselines are opaque enough to be structure, not a hint.
    private const float BaselineDiscoveryDark = 0.38f;
    private const float BaselineDiscoveryLight = 0.46f;
    private const float BaselineQueueDark = 0.24f;
    private const float BaselineQueueLight = 0.30f;
    private const float BaselineWorkerDark = 0.30f;
    private const float BaselineWorkerLight = 0.36f;

    private static readonly float[] DiscoveryDash = [3f, 3f];
    private static readonly float[] ConnectorDash = [2f, 3f];

    private const int ScannerTailSegments = 9;
    private const float ScannerTailDegrees = 78f;
    private const float WorkerGapDegrees = 12f;

    private readonly RadarPipelineState _state;

    public PipelineRadarDrawable(RadarPipelineState state) => _state = state;

    /// <summary>
    /// Which palette to paint with. The host sets it from the app theme and re-applies it on
    /// <c>RequestedThemeChanged</c>; it is a plain field read in the draw loop, so switching themes
    /// costs nothing beyond one invalidation.
    /// </summary>
    public bool IsDarkTheme { get; set; } = true;

    private Color DiscoveryColor => IsDarkTheme ? DiscoveryDark : DiscoveryLight;

    private Color QueueColor => IsDarkTheme ? QueueDark : QueueLight;

    private Color WorkerColor => IsDarkTheme ? WorkerDark : WorkerLight;

    private Color CompletedColor => IsDarkTheme ? CompletedDark : CompletedLight;

    private Color ErrorColor => IsDarkTheme ? ErrorDark : ErrorLight;

    /// <summary>The snapshot sweep has to contrast with the panel, not with the dark theme only.</summary>
    private Color SweepColor => IsDarkTheme ? Colors.White : Color.FromArgb("#334155");

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        var cx = dirtyRect.Center.X;
        var cy = dirtyRect.Center.Y;
        var radius = (Math.Min(dirtyRect.Width, dirtyRect.Height) / 2f) - 4f;
        if (radius <= 2)
        {
            return;
        }

        canvas.Antialias = true;

        DrawDiscoveryRing(canvas, cx, cy, radius);
        DrawQueueRing(canvas, cx, cy, radius);
        DrawWorkerRing(canvas, cx, cy, radius);
        DrawFocusConnector(canvas, cx, cy, radius);
        DrawPulses(canvas, cx, cy, radius);
        DrawTokens(canvas, cx, cy, radius);

        if (_state.IsSnapshotActive)
        {
            DrawSnapshotSweep(canvas, cx, cy, radius);
        }
    }

    // ------------------------------------------------------------------ rings

    private void DrawDiscoveryRing(ICanvas canvas, float cx, float cy, float radius)
    {
        var r = radius * (float)RadarPipelineState.DiscoveryRadius;
        var dim = DimFactor(RadarRegion.Discovery, -1);
        var emphasis = (float)_state.DiscoveryEmphasis;

        // Idle: a static dashed ring with an extremely subtle ambient breath - no rotation.
        var baseline = IsDarkTheme ? BaselineDiscoveryDark : BaselineDiscoveryLight;
        var ambient = baseline + (0.05f * (float)Math.Sin(_state.AmbientPhase * 1.1));
        canvas.StrokeDashPattern = DiscoveryDash;
        canvas.StrokeSize = 1.8f + (0.6f * emphasis);
        canvas.StrokeColor = DiscoveryColor.WithAlpha(Math.Min(1f, (ambient + (0.25f * emphasis)) * dim));
        canvas.DrawCircle(cx, cy, r);
        canvas.StrokeDashPattern = null;

        var intensity = (float)_state.ScannerIntensity;
        if (intensity <= 0.01f)
        {
            return;
        }

        // Active scan: one continuous head with a soft fading trail. The angle is derived from the
        // animation progress, so the wrap from 360 back to 0 is invisible.
        var head = _state.ScannerAngle;
        var chunk = ScannerTailDegrees / ScannerTailSegments;
        canvas.StrokeLineCap = LineCap.Round;
        canvas.StrokeSize = 2.2f + (0.6f * emphasis);

        for (var i = 0; i < ScannerTailSegments; i++)
        {
            var fade = 1f - (i / (float)ScannerTailSegments);
            var alpha = fade * fade * 0.85f * intensity * dim;
            if (alpha < 0.01f)
            {
                continue;
            }

            canvas.StrokeColor = DiscoveryColor.WithAlpha(alpha);
            var segEnd = head - (i * chunk);
            DrawArcSegment(canvas, cx, cy, r, segEnd - chunk, segEnd);
        }

        canvas.StrokeLineCap = LineCap.Butt;

        // Bright scanner head.
        Polar(cx, cy, r, head, out var hx, out var hy);
        canvas.FillColor = DiscoveryColor.WithAlpha(0.25f * intensity * dim);
        canvas.FillCircle(hx, hy, 3.2f);
        canvas.FillColor = DiscoveryColor.WithAlpha(intensity * dim);
        canvas.FillCircle(hx, hy, 1.5f);
    }

    private void DrawQueueRing(ICanvas canvas, float cx, float cy, float radius)
    {
        var r = radius * (float)RadarPipelineState.QueueRadius;
        var dim = DimFactor(RadarRegion.Queue, -1);
        var emphasis = (float)_state.QueueEmphasis;

        canvas.StrokeSize = 3.4f + (1.2f * emphasis);
        canvas.StrokeColor = QueueColor.WithAlpha((IsDarkTheme ? BaselineQueueDark : BaselineQueueLight) * dim);
        canvas.DrawCircle(cx, cy, r);

        var utilisation = (float)_state.QueueDisplayed;
        if (utilisation <= 0.004f)
        {
            return;
        }

        // Arc length == backlog utilisation. The value is always the interpolated one.
        var start = -90f;
        var end = start + (utilisation * 360f);
        canvas.StrokeLineCap = LineCap.Round;
        canvas.StrokeColor = QueueColor.WithAlpha((0.75f + (0.25f * emphasis)) * dim);
        DrawArcSegment(canvas, cx, cy, r, start, end);
        canvas.StrokeLineCap = LineCap.Butt;

        Polar(cx, cy, r, end, out var ex, out var ey);
        canvas.FillColor = QueueColor.WithAlpha(0.22f * dim);
        canvas.FillCircle(ex, ey, 3f + emphasis);
        canvas.FillColor = QueueColor.WithAlpha(0.95f * dim);
        canvas.FillCircle(ex, ey, 1.6f);
    }

    private void DrawWorkerRing(ICanvas canvas, float cx, float cy, float radius)
    {
        var r = radius * (float)RadarPipelineState.WorkerRadius;
        var sweep = (360f / RadarPipelineState.WorkerCount) - WorkerGapDegrees;

        for (var i = 0; i < RadarPipelineState.WorkerCount; i++)
        {
            var worker = _state.Workers[i];
            var dim = DimFactor(RadarRegion.Worker, i);
            var emphasis = (float)worker.Emphasis;
            var intensity = (float)worker.Intensity;
            var color = ColorFor(worker.State);

            var start = -90f + (i * 120f) + (WorkerGapDegrees / 2f);
            var end = start + sweep;

            // Idle: low intensity, static.
            canvas.StrokeSize = 4.5f + (1.5f * emphasis);
            var workerBaseline = IsDarkTheme ? BaselineWorkerDark : BaselineWorkerLight;
            canvas.StrokeColor = color.WithAlpha(Math.Min(1f, (workerBaseline + (0.14f * emphasis)) * dim));
            DrawArcSegment(canvas, cx, cy, r, start, end);

            if (intensity > 0.01f)
            {
                // Processing: brighter, gently breathing (per-worker phase offset), plus a soft glow.
                var breath = worker.State == RadarWorkerState.Processing
                    ? 0.82f + (0.18f * (float)Math.Sin(worker.BreathPhase * 2.4))
                    : 1f;

                canvas.StrokeSize = 6f + (intensity * 2f) + (1.5f * emphasis);
                canvas.StrokeColor = color.WithAlpha(0.12f * intensity * breath * dim);
                DrawArcSegment(canvas, cx, cy, r, start, end);

                canvas.StrokeSize = 4.5f + (intensity * 1.4f) + (1.2f * emphasis);
                canvas.StrokeColor = color.WithAlpha(((0.45f * intensity * breath) + (0.25f * emphasis)) * dim);
                DrawArcSegment(canvas, cx, cy, r, start, end);

                if (worker.State == RadarWorkerState.Processing)
                {
                    // A small highlight travelling through the segment - restrained, never a flash.
                    var span = sweep * 0.28f;
                    var travel = (float)worker.HighlightProgress * (sweep + span);
                    var hStart = Math.Max(start, start + travel - span);
                    var hEnd = Math.Min(end, start + travel);
                    if (hEnd > hStart)
                    {
                        canvas.StrokeLineCap = LineCap.Round;
                        canvas.StrokeSize = 2.4f;
                        canvas.StrokeColor = color.WithAlpha(0.9f * intensity * dim);
                        DrawArcSegment(canvas, cx, cy, r, hStart, hEnd);
                        canvas.StrokeLineCap = LineCap.Butt;
                    }
                }
            }
        }
    }

    // ------------------------------------------------------------------ overlays

    private void DrawPulses(ICanvas canvas, float cx, float cy, float radius)
    {
        foreach (var pulse in _state.Pulses)
        {
            if (!pulse.IsActive)
            {
                continue;
            }

            // Ease-out expansion with a linear fade.
            var t = (float)RadarPipelineState.Ease(RadarEase.Out, pulse.Progress);
            var alpha = 1f - (float)pulse.Progress;

            if (pulse.Kind == RadarPulseKind.Detection)
            {
                var pr = radius * (float)pulse.Radius;
                Polar(cx, cy, pr, pulse.Angle, out var px, out var py);
                canvas.StrokeSize = 1.2f;
                canvas.StrokeColor = DiscoveryColor.WithAlpha(alpha * 0.9f);
                canvas.DrawCircle(px, py, 1.5f + (t * 7f));
                canvas.FillColor = DiscoveryColor.WithAlpha(alpha * 0.5f);
                canvas.FillCircle(px, py, 1.6f * (1f - t));
                continue;
            }

            // Completion: arc pulse expanding outward from the worker segment.
            var worker = pulse.WorkerIndex >= 0 ? _state.Workers[pulse.WorkerIndex] : null;
            var color = ColorFor(worker?.State ?? RadarWorkerState.Completed);
            var baseR = radius * (float)pulse.Radius;
            var pulseR = baseR + (t * radius * 0.42f);
            var sweep = (360f / RadarPipelineState.WorkerCount) - WorkerGapDegrees;
            var start = -90f + (pulse.WorkerIndex * 120f) + (WorkerGapDegrees / 2f);

            canvas.StrokeLineCap = LineCap.Round;
            canvas.StrokeSize = 1.6f * (1f - t) + 0.4f;
            canvas.StrokeColor = color.WithAlpha(alpha * 0.75f);
            DrawArcSegment(canvas, cx, cy, pulseR, start, start + sweep);
            canvas.StrokeLineCap = LineCap.Butt;
        }
    }

    private void DrawTokens(ICanvas canvas, float cx, float cy, float radius)
    {
        foreach (var token in _state.Tokens)
        {
            if (!token.IsActive || token.Opacity <= 0.01)
            {
                continue;
            }

            var dim = token.WorkerIndex >= 0
                ? DimFactor(RadarRegion.Worker, token.WorkerIndex)
                : DimFactor(RadarRegion.Queue, -1);

            var color = token.WorkerIndex >= 0
                ? ColorFor(_state.Workers[token.WorkerIndex].State)
                : token.Stage is RadarTokenStage.Detected or RadarTokenStage.EnteringQueue
                    ? DiscoveryColor
                    : QueueColor;

            var r = radius * (float)token.Radius;
            Polar(cx, cy, r, token.Angle, out var x, out var y);

            var alpha = (float)token.Opacity * dim;
            var size = 1.7f * (float)token.Scale;

            canvas.FillColor = color.WithAlpha(alpha * 0.28f);
            canvas.FillCircle(x, y, size * 2.4f);
            canvas.FillColor = color.WithAlpha(alpha);
            canvas.FillCircle(x, y, size);

            if (token.IsSelected || token.IsHovered)
            {
                canvas.StrokeSize = 1f;
                canvas.StrokeColor = color.WithAlpha(alpha * 0.8f);
                canvas.DrawCircle(x, y, size * 3f);
            }
        }
    }

    /// <summary>
    /// Focus mode: a subtle connector showing "queue item -> worker N", so the rings read as one
    /// connected system rather than three separate circles.
    /// </summary>
    private void DrawFocusConnector(ICanvas canvas, float cx, float cy, float radius)
    {
        var reveal = (float)_state.ConnectorReveal;
        if (_state.FocusedWorker < 0 || reveal <= 0.02f)
        {
            return;
        }

        var worker = _state.FocusedWorker;
        var token = _state.TokenOfWorker(worker);
        var fromAngle = token?.Angle ?? RadarPipelineState.WorkerCenterAngle(worker);
        var fromR = radius * (float)RadarPipelineState.QueueRadius;
        var toR = radius * (float)RadarPipelineState.WorkerRadius;

        Polar(cx, cy, fromR, fromAngle, out var x1, out var y1);
        Polar(cx, cy, toR, RadarPipelineState.WorkerCenterAngle(worker), out var x2, out var y2);

        var x = x1 + ((x2 - x1) * reveal);
        var y = y1 + ((y2 - y1) * reveal);

        canvas.StrokeDashPattern = ConnectorDash;
        canvas.StrokeSize = 1f;
        canvas.StrokeColor = ColorFor(_state.Workers[worker].State).WithAlpha(0.55f * reveal);
        canvas.DrawLine(x1, y1, x, y);
        canvas.StrokeDashPattern = null;
    }

    private void DrawSnapshotSweep(ICanvas canvas, float cx, float cy, float radius)
    {
        var head = -90 + (_state.SweepProgress * 360);
        canvas.StrokeSize = 1.4f;

        for (var i = 0; i < 12; i++)
        {
            var alpha = (1f - (i / 12f)) * (IsDarkTheme ? 0.18f : 0.26f);
            Polar(cx, cy, radius, head - (i * 2.5), out var x, out var y);
            canvas.StrokeColor = SweepColor.WithAlpha(alpha);
            canvas.DrawLine(cx, cy, x, y);
        }

        Polar(cx, cy, radius, head, out var nx, out var ny);
        canvas.StrokeColor = SweepColor.WithAlpha(0.55f);
        canvas.DrawLine(cx, cy, nx, ny);
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>
    /// While a region is hovered or a worker is focused, unrelated elements are only *slightly*
    /// quietened - level 1 (data) motion must never be drowned out by level 2/3 effects.
    /// </summary>
    private float DimFactor(RadarRegion region, int workerIndex)
    {
        var dimming = (float)_state.Dimming;
        if (dimming <= 0.01f)
        {
            return 1f;
        }

        var isSubject = _state.FocusedWorker >= 0
            ? region == RadarRegion.Worker && workerIndex == _state.FocusedWorker
            : region == _state.HoveredRegion && (region != RadarRegion.Worker || workerIndex == _state.HoveredWorker);

        return isSubject ? 1f : 1f - (0.45f * dimming);
    }

    private Color ColorFor(RadarWorkerState state) => state switch
    {
        RadarWorkerState.Processing => WorkerColor,
        RadarWorkerState.Completed => CompletedColor,
        RadarWorkerState.Error => ErrorColor,
        _ => WorkerColor
    };

    /// <summary>Screen-space polar helper: 0 deg = 3 o'clock, positive = clockwise.</summary>
    private static void Polar(float cx, float cy, float r, double angleDegrees, out float x, out float y)
    {
        var rad = angleDegrees * Math.PI / 180.0;
        x = cx + (float)(Math.Cos(rad) * r);
        y = cy + (float)(Math.Sin(rad) * r);
    }

    /// <summary>
    /// Draws a clockwise arc between two screen-space angles. Microsoft.Maui.Graphics measures its
    /// arc angles counterclockwise, hence the sign flip.
    /// </summary>
    private static void DrawArcSegment(ICanvas canvas, float cx, float cy, float r, double fromDegrees, double toDegrees)
    {
        canvas.DrawArc(cx - r, cy - r, r * 2, r * 2, (float)-fromDegrees, (float)-toDegrees, true, false);
    }
}
