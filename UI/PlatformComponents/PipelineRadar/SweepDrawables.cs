using Microsoft.Maui.Graphics;

namespace MostaqlK.UI.PlatformComponents.PipelineRadar;

/// <summary>
/// Shared palette helpers for the two rotating overlay layers, mirroring exactly what
/// <see cref="PipelineRadarDrawable"/> painted while the ticker still spun them in-canvas
/// (dev30 de-managed ticker, plan step 4): same hues, same tail geometry, same alpha ramps —
/// only the rotation mechanism moved to the Windows compositor.
/// </summary>
internal static class RadarSweepPalette
{
    // Discovery ring legacy light hue + snapshot sweep contrast hue, kept identical to the
    // main drawable's private constants so the overlays and the rings never disagree.
    public static readonly Color DiscoveryLight = Color.FromArgb("#1D6FA5");

    public static Color DiscoveryColor(bool isDarkTheme) =>
        isDarkTheme ? MostaqlK.UI.DesignSystem.BrandTokens.BrandPalette.AccentDark : DiscoveryLight;

    public static Color SweepColor(bool isDarkTheme) =>
        isDarkTheme ? Colors.White : Color.FromArgb("#334155");

    public static void Polar(float cx, float cy, float r, double angleDegrees, out float x, out float y)
    {
        var rad = angleDegrees * Math.PI / 180.0;
        x = cx + (float)(Math.Cos(rad) * r);
        y = cy + (float)(Math.Sin(rad) * r);
    }

    public static void DrawArcSegment(ICanvas canvas, float cx, float cy, float r, double fromDegrees, double toDegrees) =>
        canvas.DrawArc(cx - r, cy - r, r * 2, r * 2, (float)-fromDegrees, (float)-toDegrees, true, false);

    /// <summary>Ring radius the scanner rides — same math as the main drawable's discovery ring.</summary>
    public static float ScannerRadius(RectF dirtyRect) =>
        (MathF.Min(dirtyRect.Width, dirtyRect.Height) / 2f) - 4f;
}

/// <summary>
/// The scanner head + fading tail painted <b>statically at 12 o'clock</b> (angle -90) on its own
/// transparent GraphicsView. On Windows the whole layer rotates via one looping compositor
/// <c>RotationAngleInDegrees</c> key-frame (4s/revolution) — this drawable never sees a per-frame
/// invalidation from the rotation itself, only from the intensity ramp / theme / brand changes.
/// Where there is no compositor (mobile, reduced motion) the managed model still advances the
/// angle, but that path renders the head statically at rest, matching the old reduced-motion
/// behavior (a frozen head, honest and quiet).
/// </summary>
public sealed class ScannerSweepDrawable : IDrawable
{
    private const int TailSegments = 9;
    private const float TailDegrees = 78f;

    private readonly RadarPipelineState _state;

    public ScannerSweepDrawable(RadarPipelineState state) => _state = state;

    /// <summary>Set by the host alongside the main drawable's; mirrors the app theme.</summary>
    public bool IsDarkTheme { get; set; } = true;

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        var intensity = (float)_state.ScannerIntensity;
        if (intensity <= 0.01f)
        {
            return;
        }

        var radius = RadarSweepPalette.ScannerRadius(dirtyRect);
        if (radius <= 2)
        {
            return;
        }

        var cx = dirtyRect.Center.X;
        var cy = dirtyRect.Center.Y;
        var r = radius * (float)RadarPipelineState.DiscoveryRadius;
        var color = RadarSweepPalette.DiscoveryColor(IsDarkTheme);

        // Same hover/focus quietening the main drawable applies to the discovery ring, so the
        // head never outranks a focused worker (levels rule: effects below data).
        var emphasis = (float)_state.DiscoveryEmphasis;
        var dimming = (float)_state.Dimming;
        var isSubject = _state.FocusedWorker < 0 && _state.HoveredRegion == RadarRegion.Discovery;
        var dim = dimming <= 0.01f ? 1f : (isSubject ? 1f : 1f - (0.45f * dimming));

        canvas.Antialias = true;

        // Drawn at the canonical rest angle (-90, straight up). The compositor rotation of the
        // hosting layer IS the head's travel, so the wrap from 360 back to 0 can never be visible.
        const float head = -90f;
        var chunk = TailDegrees / TailSegments;
        canvas.StrokeLineCap = LineCap.Round;
        canvas.StrokeSize = 2.2f + (0.6f * emphasis);

        for (var i = 0; i < TailSegments; i++)
        {
            var fade = 1f - (i / (float)TailSegments);
            var alpha = fade * fade * 0.85f * intensity * dim;
            if (alpha < 0.01f)
            {
                continue;
            }

            canvas.StrokeColor = color.WithAlpha(alpha);
            var segEnd = head - (i * chunk);
            RadarSweepPalette.DrawArcSegment(canvas, cx, cy, r, segEnd - chunk, segEnd);
        }

        canvas.StrokeLineCap = LineCap.Butt;

        RadarSweepPalette.Polar(cx, cy, r, head, out var hx, out var hy);
        canvas.FillColor = color.WithAlpha(0.25f * intensity * dim);
        canvas.FillCircle(hx, hy, 3.2f);
        canvas.FillColor = color.WithAlpha(intensity * dim);
        canvas.FillCircle(hx, hy, 1.5f);
    }
}

/// <summary>
/// The snapshot/diff sweep (12 fading rays + a brighter head line) painted <b>statically pointing
/// at 12 o'clock</b> on its own transparent GraphicsView; Windows rotates the layer on the
/// compositor (1.5s/revolution) while <see cref="RadarPipelineState.IsSnapshotActive"/>. Reduced
/// motion / no compositor ⇒ the honest static sweep (the old reduced-motion behavior painted no
/// moving sweep at all; a still ray set communicates "snapshot held" without motion).
/// </summary>
public sealed class SnapshotSweepDrawable : IDrawable
{
    private readonly RadarPipelineState _state;

    public SnapshotSweepDrawable(RadarPipelineState state) => _state = state;

    public bool IsDarkTheme { get; set; } = true;

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        if (!_state.IsSnapshotActive)
        {
            return;
        }

        var radius = RadarSweepPalette.ScannerRadius(dirtyRect);
        if (radius <= 2)
        {
            return;
        }

        var cx = dirtyRect.Center.X;
        var cy = dirtyRect.Center.Y;
        var color = RadarSweepPalette.SweepColor(IsDarkTheme);

        canvas.Antialias = true;
        canvas.StrokeSize = 1.4f;

        const float head = -90f;
        for (var i = 0; i < 12; i++)
        {
            var alpha = (1f - (i / 12f)) * (IsDarkTheme ? 0.18f : 0.26f);
            RadarSweepPalette.Polar(cx, cy, radius, head - (i * 2.5f), out var x, out var y);
            canvas.StrokeColor = color.WithAlpha(alpha);
            canvas.DrawLine(cx, cy, x, y);
        }

        RadarSweepPalette.Polar(cx, cy, radius, head, out var nx, out var ny);
        canvas.StrokeColor = color.WithAlpha(0.55f);
        canvas.DrawLine(cx, cy, nx, ny);
    }
}
