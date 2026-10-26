using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml.Hosting;

namespace MostaqlK.UI.PlatformComponents.PipelineRadar;

/// <summary>
/// Windows half of the radar's rotation mechanism (dev30 de-managed ticker, plan step 4): the
/// scanner head and the snapshot sweep ride one LOOPING compositor
/// <c>RotationAngleInDegrees</c> scalar key-frame animation each
/// (<c>IterationCount = uint.MaxValue</c> — infinite, linear, render-thread) on the overlay canvas's
/// own <c>Visual</c> — the class animation-safety rule 3 approves: the property is a built-in
/// member of an existing composition visual, so a spinning radar allocates ZERO managed objects
/// per frame, unlike the retired 16ms <c>Animation.Commit</c>→<c>Invalidate</c> loop.
/// <para>
/// Stop = <c>StopAnimation</c> + <c>Dispose</c> on the UI thread (never left to the CLR finalizer,
/// the postmortem's fail-fast class) and is driven exclusively through the shared
/// <c>SyncRotations</c> gate: park-on-settle, scan/snapshot end, tray-hide, freeze, reduced motion.
/// Infinite ⇒ never ambient-suspended (rule 1) — every one of those events removes the animation
/// outright rather than letting it run unseen.
/// </para>
/// </summary>
public partial class PipelineRadar
{
    private const string RotationPropertyName = "RotationAngleInDegrees";

    // Field types stay on the PROJECTION-SAFE base (<c>CompositionAnimation</c>, same type name the
    // reveal unit stores) — the concrete CreateScalarKeyFrameAnimation return type is consumed via
    // `var` at creation exactly like SourceMorphTransition does, never named in a signature.
    private Microsoft.UI.Composition.Visual? _scannerVisual;
    private CompositionAnimation? _scannerAnimation;
    private Microsoft.UI.Composition.Visual? _sweepVisual;
    private CompositionAnimation? _sweepAnimation;

    partial void StartScannerRotation()
        => StartRotation(ScannerCanvas, ref _scannerVisual, ref _scannerAnimation, RadarPipelineState.ScannerPeriodSeconds);

    partial void StopScannerRotation()
        => StopRotation(ref _scannerVisual, ref _scannerAnimation);

    partial void StartSweepRotation()
        => StartRotation(SweepCanvas, ref _sweepVisual, ref _sweepAnimation, RadarPipelineState.SweepPeriodSeconds);

    partial void StopSweepRotation()
        => StopRotation(ref _sweepVisual, ref _sweepAnimation);

    partial void UpdateRotationCenters()
    {
        // The pivot lives in raw-pixel compositor space and the dial can resize under a live scan
        // (dashboard Diameter) — re-center both live pivots from the visuals' current sizes.
        SetRotationCenter(_scannerVisual);
        SetRotationCenter(_sweepVisual);
    }

    private static void StartRotation(
        GraphicsView layer,
        ref Microsoft.UI.Composition.Visual? visual,
        ref CompositionAnimation? animation,
        double periodSeconds)
    {
        try
        {
            if (animation is not null)
            {
                return; // already looping — idempotent start
            }

            if (layer.Handler?.PlatformView is not Microsoft.UI.Xaml.UIElement element)
            {
                return; // not realized; the next event-driven sync retries
            }

            var current = ElementCompositionPreview.GetElementVisual(element);
            var compositor = current?.Compositor;
            if (current is null || compositor is null)
            {
                return;
            }

            var loop = compositor.CreateScalarKeyFrameAnimation();
            loop.Duration = TimeSpan.FromSeconds(Math.Max(0.5, periodSeconds));
            loop.IterationCount = -1; // this projection surfaces the WinRT UINT32 as int; -1 == 0xFFFFFFFF == infinite
            loop.InsertKeyFrame(0.0f, 0.0f);
            loop.InsertKeyFrame(1.0f, 360.0f); // linear by default — a radar sweep never eases

            visual = current;
            SetRotationCenter(current);
            current.StartAnimation(RotationPropertyName, loop);
            animation = loop;
        }
        catch (Exception)
        {
            // A rotation that failed to (re)start must not leave half-ownership behind: the next
            // sync cleanly re-attempts, and the managed-fallback angle stays coherent either way.
            StopRotation(ref visual, ref animation);
        }
    }

    private static void StopRotation(
        ref Microsoft.UI.Composition.Visual? visual,
        ref CompositionAnimation? animation)
    {
        try
        {
            if (visual is not null && animation is not null)
            {
                visual.StopAnimation(RotationPropertyName);
            }
        }
        catch (Exception)
        {
            // visual tree may already be gone; the dispose below is the part that matters
        }

        try
        {
            // Disposed ON THE UI THREAD (caller is dispatcher-bound) — never left for the
            // finalizer thread with a live composition object (the crash class).
            animation?.Dispose();
        }
        catch (Exception)
        {
        }

        visual = null;
        animation = null;
    }

    private static void SetRotationCenter(Microsoft.UI.Composition.Visual? visual)
    {
        if (visual is null)
        {
            return;
        }

        // XAML-hosted visuals mirror their element's render size in compositor pixels, so the
        // dial's pivot is simply half the live size — correct at any DPI, never a load-time value.
        var size = visual.Size;
        if (size.X > 1f && size.Y > 1f)
        {
            visual.CenterPoint = new Vector3(size.X / 2f, size.Y / 2f, 0f);
        }
    }
}
