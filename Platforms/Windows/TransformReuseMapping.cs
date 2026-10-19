using Microsoft.Maui.Handlers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace MostaqlK.Platforms.Windows;

/// <summary>
/// Replaces MAUI's Windows transform mapping (<c>TranslationX/Y</c>, <c>Scale/ScaleX/ScaleY</c>,
/// <c>Rotation/RotationX/RotationY</c>, <c>AnchorX/Y</c>) with one that <b>mutates a single
/// <see cref="CompositeTransform"/> per element</b> instead of allocating new XAML objects.
/// <para>
/// Why (root cause of the "tray icon vanished after hours in the background" crash): MAUI's stock
/// <c>TransformationExtensions.UpdateTransformation</c> allocates a <c>new ScaleTransform</c> <i>and</i> a
/// <c>new CompositeTransform</c> on every single property change. Every running <c>TranslateToAsync</c>/
/// <c>RotateToAsync</c>/ticker in the app (the unread-card <c>NewRibbonBadge</c> sweep loops forever, plus
/// <c>ShimmerBox</c>, <c>EnrichmentShimmerOverlay</c>, the <c>LastScanStatus</c> spinner on every poll) therefore
/// produced ~120 native XAML DependencyObjects per second per animated view, for as long as the process lived -
/// hidden window or not. Each one is released by the CLR finalizer thread and marshalled back through WinUI's
/// <c>UIAffinityReleaseQueue</c>. Both captured crash dumps die exactly there: 2026-10-14 in
/// <c>UIAffinityReleaseQueue::DoCleanup</c> (releasing a transform) and 2026-10-19 on the finalizer thread in
/// <c>ctl::ComBase::ReleaseImpl</c> → <c>FailFastWithStowedExceptions</c> (<c>0xC000027B</c>). That fail-fast
/// cannot be caught by any handler, so the only real fix is to stop producing that garbage at all.
/// </para>
/// <para>
/// Visual output is identical to MAUI's: same origin (<c>RenderTransformOrigin</c> = anchor), same scale,
/// rotation and translation composition, and the same <see cref="PlaneProjection"/> path when a 3D rotation
/// is requested (also reused instead of re-allocated). Must be installed before the first handler is created.
/// </para>
/// </summary>
internal static class TransformReuseMapping
{
    private static bool _installed;

    private static readonly string[] TransformKeys =
    {
        nameof(IView.TranslationX),
        nameof(IView.TranslationY),
        nameof(IView.Scale),
        nameof(IView.ScaleX),
        nameof(IView.ScaleY),
        nameof(IView.Rotation),
        nameof(IView.RotationX),
        nameof(IView.RotationY),
        nameof(IView.AnchorX),
        nameof(IView.AnchorY),
    };

    public static void Install()
    {
        if (_installed)
        {
            return;
        }

        _installed = true;
        foreach (var key in TransformKeys)
        {
            ViewHandler.ViewMapper.ModifyMapping(key, (handler, view, _) => Apply(handler, view));
        }
    }

    private static void Apply(IViewHandler handler, IView view)
    {
        try
        {
            if ((handler.ContainerView ?? handler.PlatformView) is FrameworkElement element)
            {
                Update(element, view);
            }
        }
        catch (Exception ex)
        {
            // A torn-down native peer must never take the process down over a decorative transform.
            MostaqlK.Services.Diagnostics.CrashReporter.Report("TransformReuseMapping.Apply", ex, isFatal: false);
        }
    }

    private static void Update(FrameworkElement element, IView view)
    {
        var rotationX = view.RotationX;
        var rotationY = view.RotationY;
        var rotation = view.Rotation;
        var translationX = view.TranslationX;
        var translationY = view.TranslationY;
        var scaleX = view.Scale * view.ScaleX;
        var scaleY = view.Scale * view.ScaleY;

        var isIdentity = rotationX % 360 == 0 && rotationY % 360 == 0 && rotation % 360 == 0 &&
                         translationX == 0 && translationY == 0 && scaleX == 1 && scaleY == 1;

        if (isIdentity)
        {
            // Keep the existing transform object and zero it out instead of dropping it: an animation
            // passing through identity on every loop would otherwise recreate it on the next frame.
            if (element.RenderTransform is CompositeTransform existing)
            {
                SetComposite(existing, 0, 1, 1, 0, 0);
            }
            else if (element.RenderTransform is not null)
            {
                element.RenderTransform = null;
            }

            if (element.Projection is not null)
            {
                element.Projection = null;
            }

            return;
        }

        var origin = new global::Windows.Foundation.Point(view.AnchorX, view.AnchorY);
        if (element.RenderTransformOrigin != origin)
        {
            element.RenderTransformOrigin = origin;
        }

        var composite = element.RenderTransform as CompositeTransform;
        if (composite is null)
        {
            composite = new CompositeTransform();
            element.RenderTransform = composite;
        }

        if (Math.Abs(rotationX) != 0 || Math.Abs(rotationY) != 0)
        {
            if (double.IsNaN(rotationX) || double.IsNaN(rotationY) || double.IsNaN(rotation))
            {
                return;
            }

            // MAUI's 3D path: scale stays on the render transform, everything else goes to the projection.
            SetComposite(composite, 0, scaleX, scaleY, 0, 0);

            var projection = element.Projection as PlaneProjection;
            if (projection is null)
            {
                projection = new PlaneProjection();
                element.Projection = projection;
            }

            SetIfChanged(projection.CenterOfRotationX, view.AnchorX, v => projection.CenterOfRotationX = v);
            SetIfChanged(projection.CenterOfRotationY, view.AnchorY, v => projection.CenterOfRotationY = v);
            SetIfChanged(projection.GlobalOffsetX, translationX, v => projection.GlobalOffsetX = v);
            SetIfChanged(projection.GlobalOffsetY, translationY, v => projection.GlobalOffsetY = v);
            SetIfChanged(projection.RotationX, -rotationX, v => projection.RotationX = v);
            SetIfChanged(projection.RotationY, -rotationY, v => projection.RotationY = v);
            SetIfChanged(projection.RotationZ, -rotation, v => projection.RotationZ = v);
            return;
        }

        if (element.Projection is not null)
        {
            element.Projection = null;
        }

        SetComposite(composite, rotation, scaleX, scaleY, translationX, translationY);
    }

    private static void SetComposite(CompositeTransform t, double rotation, double scaleX, double scaleY, double translateX, double translateY)
    {
        SetIfChanged(t.Rotation, rotation, v => t.Rotation = v);
        SetIfChanged(t.ScaleX, scaleX, v => t.ScaleX = v);
        SetIfChanged(t.ScaleY, scaleY, v => t.ScaleY = v);
        SetIfChanged(t.TranslateX, translateX, v => t.TranslateX = v);
        SetIfChanged(t.TranslateY, translateY, v => t.TranslateY = v);
    }

    private static void SetIfChanged(double current, double next, Action<double> set)
    {
        if (!current.Equals(next))
        {
            set(next);
        }
    }
}
