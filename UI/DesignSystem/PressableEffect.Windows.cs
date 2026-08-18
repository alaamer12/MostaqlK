using System.Reflection;

namespace MostaqlK.UI.DesignSystem;

/// <summary>
/// Windows-only half of <see cref="PressableEffect"/>: hover background highlight, cross-hover
/// coordination between nested pressables, and the WinUI pointer cursor. Hover is a mouse/pointer
/// concept with no touch equivalent, so none of this applies on Android — see
/// <see cref="PressableEffect.Android.cs"/> for that platform's (intentionally different) native
/// press feedback story.
/// </summary>
public partial class PressableEffect
{
    private static readonly PropertyInfo? ProtectedCursorProperty =
        typeof(Microsoft.UI.Xaml.UIElement).GetProperty(
            "ProtectedCursor",
            BindingFlags.Instance | BindingFlags.NonPublic);

    private Color? _originalBackgroundColor;
    private bool _cursorApplied;

    // Whether THIS view's own highlight is currently being suppressed because a nested
    // pressable descendant (e.g. ProjectCard's "عرض في مستقل" chip inside the card) is being
    // hovered/pressed - see SuppressForChildHover/ResumeAfterChildHover below.
    private bool _suppressedByChildHover;

    partial void OnHandlerAttachedForPlatform()
    {
        if (_associatedView?.Handler != null)
        {
            _cursorApplied = false;
        }
    }

    partial void HandlePointerEntered()
    {
        // WinUI's PointerEntered/Exited are geometry-bound to each element's own bounding box:
        // they don't fire on an ancestor just because the pointer moved onto a nested child that's
        // still visually inside the ancestor's rect (e.g. moving from a ProjectCard's body onto its
        // "عرض في مستقل" chip button). So when THIS view's own hover starts, tell the nearest
        // ancestor that also has a PressableEffect to step aside instead of showing both highlights
        // stacked on top of each other.
        FindAncestorPressable()?.SuppressForChildHover();

        if (ApplyHoverHighlight)
        {
            // Always use current color as base for hover, but avoid using a previous hover color
            var currentColor = _associatedView!.BackgroundColor ?? Colors.Transparent;

            // If we are already hovered, don't re-store original color
            if (_originalBackgroundColor == null)
            {
                _originalBackgroundColor = currentColor;
            }

            ApplyHighlightNow();
        }

        // Apply cursor only once
        if (!_cursorApplied)
        {
            ApplyPlatformCursor();
            _cursorApplied = true;
        }
    }

    partial void HandlePointerExited()
    {
        // Restore background color immediately
        if (ApplyHoverHighlight && _originalBackgroundColor != null)
        {
            _associatedView!.BackgroundColor = _originalBackgroundColor;
            _originalBackgroundColor = null;
        }

        // Pointer left this (child) view but is still within the ancestor's bounds (that's exactly
        // why WinUI didn't already re-trigger the ancestor's own PointerEntered) - let the
        // ancestor's highlight take back over.
        FindAncestorPressable()?.ResumeAfterChildHover();
    }

    private void ApplyHighlightNow()
    {
        if (_associatedView == null || _originalBackgroundColor == null)
        {
            return;
        }

        // Modern elegant hover color: slightly lighter in dark, slightly darker in light
        var defaultHover = Application.Current?.RequestedTheme == AppTheme.Dark
            ? Color.FromArgb("#15FFFFFF") // Subtle light overlay for dark theme
            : Color.FromArgb("#0A000000"); // Very subtle dark overlay for light theme

        var highlight = HoverColor ?? defaultHover;

        // If the view has a background, we should blend the highlight
        _associatedView.BackgroundColor = _originalBackgroundColor != Colors.Transparent
            ? BlendColors(_originalBackgroundColor, highlight)
            : highlight;
    }

    /// <summary>
    /// Called by a nested descendant's <see cref="PressableEffect"/> when IT starts hovering, so
    /// this (ancestor) view's own highlight doesn't stay stuck showing underneath/behind the
    /// child's own highlight for as long as the pointer sits anywhere within this view's bounds.
    /// </summary>
    internal void SuppressForChildHover()
    {
        if (_associatedView == null || _originalBackgroundColor == null)
        {
            return;
        }

        _suppressedByChildHover = true;
        _associatedView.BackgroundColor = _originalBackgroundColor;
    }

    /// <summary>
    /// Called by a nested descendant's <see cref="PressableEffect"/> when IT stops hovering
    /// (pointer exited the child but is still within this ancestor's bounds), so this view's own
    /// highlight resumes as if the pointer had just re-entered it.
    /// </summary>
    internal void ResumeAfterChildHover()
    {
        if (!_suppressedByChildHover)
        {
            return;
        }

        _suppressedByChildHover = false;
        if (ApplyHoverHighlight)
        {
            ApplyHighlightNow();
        }
    }

    /// <summary>Walks up the visual tree to find the nearest ancestor carrying its own <see cref="PressableEffect"/> (e.g. a ProjectCard's AppCard wrapping this chip button).</summary>
    private PressableEffect? FindAncestorPressable()
    {
        var parent = (_associatedView as Element)?.Parent;
        while (parent != null)
        {
            if (parent is View view)
            {
                foreach (var behavior in view.Behaviors)
                {
                    if (behavior is PressableEffect ancestorEffect && ancestorEffect != this)
                    {
                        return ancestorEffect;
                    }
                }
            }
            parent = parent.Parent;
        }
        return null;
    }

    private Color BlendColors(Color baseColor, Color overlay)
    {
        return Color.FromRgba(
            (baseColor.Red * (1 - overlay.Alpha)) + (overlay.Red * overlay.Alpha),
            (baseColor.Green * (1 - overlay.Alpha)) + (overlay.Green * overlay.Alpha),
            (baseColor.Blue * (1 - overlay.Alpha)) + (overlay.Blue * overlay.Alpha),
            Math.Max(baseColor.Alpha, overlay.Alpha)
        );
    }

    partial void ApplyPlatformCursor()
    {
#if WINDOWS
        if (_associatedView?.Handler?.PlatformView is not Microsoft.UI.Xaml.UIElement element)
        {
            return;
        }

        try
        {
            // ProtectedCursor is the correct way to set cursors on WinUI 3 elements
            // but it is protected, so we use reflection.
            ProtectedCursorProperty?.SetValue(
                element,
                Microsoft.UI.Input.InputSystemCursor.Create(
                    Microsoft.UI.Input.InputSystemCursorShape.Hand));
        }
        catch (Exception)
        {
            // Cursor shape is a nicety
        }
#endif
    }
}
