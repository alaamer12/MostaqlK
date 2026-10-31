using System.Numerics;
using System.Threading;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml.Hosting;
using MostaqlK.Services.Diagnostics;

namespace MostaqlK.UI.PlatformComponents.PipelineRadar;

/// <summary>
/// Windows half of the radar's rotation mechanism (dev30 de-managed ticker, plan step 4): the
/// scanner head and the snapshot sweep ride one LOOPING compositor
/// <c>RotationAngleInDegrees</c> scalar key-frame animation each
/// (<c>IterationCount = int.MaxValue</c> — the WinAppSDK 1.7 projection validates a positive INT32;
/// infinite, linear, render-thread) on the overlay canvas's own <c>Visual</c> — the class animation-safety rule 3 approves: the property is a built-in
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

    private partial bool TryStartScannerRotation()
        => TryStartRotation(ScannerCanvas, ref _scannerVisual, ref _scannerAnimation, RadarPipelineState.ScannerPeriodSeconds, "scanner");

    partial void StopScannerRotation()
        => StopRotation(ref _scannerVisual, ref _scannerAnimation);

    private partial bool TryStartSweepRotation()
        => TryStartRotation(SweepCanvas, ref _sweepVisual, ref _sweepAnimation, RadarPipelineState.SweepPeriodSeconds, "sweep");

    partial void StopSweepRotation()
        => StopRotation(ref _sweepVisual, ref _sweepAnimation);

    partial void UpdateRotationCenters()
    {
        // The pivot lives in raw-pixel compositor space and the dial can resize under a live scan
        // (dashboard Diameter) — re-center both live pivots from the visuals' current sizes.
        SetRotationCenter(_scannerVisual);
        SetRotationCenter(_sweepVisual);
    }

    // dev70 postmortem (action A2): the old shape threw inside a catch that continued as if nothing
    // failed — abandoning a half-built composition object each time (the off-thread finalization fuel
    // of the documented 0xC000027B fail-fast class) and leaving the caller's flag set, so every Wake
    // retried a deterministic API failure. Contract now: ownership of every native object is taken
    // BEFORE the next risky call; the catch disposes and NAMES the fault; the result reaches the
    // caller as a bool; failures are capped process-wide because a contract failure repeats
    // identically — and the repetition is the fuel.
    private static int _startFailuresThisProcess;
    private const int MaxRotationStartFailuresPerProcess = 8;

#if DEBUG
    private static readonly bool TestRadarFaultEnabled =
        string.Equals(Environment.GetEnvironmentVariable("MOSTAQLK_TEST_RADAR_FAULT"), "1", StringComparison.Ordinal);

    private static int _testRadarFaultUsed;
#endif

    private static bool TryStartRotation(
        GraphicsView layer,
        ref Microsoft.UI.Composition.Visual? visual,
        ref CompositionAnimation? animation,
        double periodSeconds,
        string dial)
    {
        try
        {
            if (animation is not null)
            {
                return true; // already looping — idempotent start
            }

            if (_startFailuresThisProcess >= MaxRotationStartFailuresPerProcess)
            {
                return false; // this build's rotation call fails deterministically; stop fueling
            }

            if (layer.Handler?.PlatformView is not Microsoft.UI.Xaml.UIElement element)
            {
                return false; // not realized; the next event-driven sync retries
            }

            var current = ElementCompositionPreview.GetElementVisual(element);
            var compositor = current?.Compositor;
            if (current is null || compositor is null)
            {
                return false;
            }

            var loop = compositor.CreateScalarKeyFrameAnimation();
            loop.Duration = TimeSpan.FromSeconds(Math.Max(0.5, periodSeconds));
            // The WinAppSDK 1.7 C# projection types IterationCount as INT32 and validates > 0 —
            // the old `-1` (thinking it wraps to UINT32_MAX) now THROWS E_INVALIDARG here. The throw
            // was swallowed by the catch below, but it abandoned the half-built KeyFrameAnimation COM
            // object, and abandoned composition objects finalize off-thread — the exact fuel of the
            // documented 0xC000027B dispatcher fail-fast (published-layout-dispatcher-failfast §10).
            // int.MaxValue is ~273 years at this duration: infinite for every purpose.
            loop.IterationCount = int.MaxValue;
            loop.InsertKeyFrame(0.0f, 0.0f);
            loop.InsertKeyFrame(1.0f, 360.0f); // linear by default — a radar sweep never eases

#if DEBUG
            if (TestRadarFaultEnabled && Interlocked.Exchange(ref _testRadarFaultUsed, 1) == 0)
            {
                throw new InvalidOperationException("injected radar rotation fault (MOSTAQLK_TEST_RADAR_FAULT=1)");
            }
#endif

            // OWNERSHIP BEFORE THE RISKY CALL: from the assignments down, every exception path runs
            // through the catch, which disposes — no path may leave a composition object orphaned.
            visual = current;
            animation = loop;
            SetRotationCenter(current);
            current.StartAnimation(RotationPropertyName, loop);
            return true;
        }
        catch (Exception ex)
        {
            _startFailuresThisProcess++;
            // A rotation that failed to (re)start must not leave half-ownership behind: dispose on
            // the UI thread, clear the caller's refs — and NAME the fault. The silent swallow is
            // exactly what hid this crash class for nine days.
            StopRotation(ref visual, ref animation);
            InteractionLogger.Fault("PipelineRadar.Rotation.Start", ex,
                new { dial, periodSeconds, failures = _startFailuresThisProcess });
            return false;
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
        catch (Exception ex)
        {
            // visual tree may already be gone; the dispose below is the part that matters
            InteractionLogger.Fault("PipelineRadar.Rotation.Stop.StopAnimation", ex);
        }

        try
        {
            // Disposed ON THE UI THREAD (caller is dispatcher-bound) — never left for the
            // finalizer thread with a live composition object (the crash class).
            animation?.Dispose();
        }
        catch (Exception ex)
        {
            InteractionLogger.Fault("PipelineRadar.Rotation.Stop.Dispose", ex);
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
