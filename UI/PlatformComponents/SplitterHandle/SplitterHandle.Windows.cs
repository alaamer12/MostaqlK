using System.Reflection;

namespace MostaqlK.UI.PlatformComponents;

/// <summary>
/// Windows-only tweaks for <see cref="SplitterHandle"/> (V1 scope): the west-east resize cursor.
/// </summary>
public partial class SplitterHandle
{
    // WinUI exposes the pointer cursor as UIElement.ProtectedCursor, which is `protected` and can
    // therefore only be set from a subclass - and MAUI's platform view is not ours to subclass.
    // Reflecting onto the setter is the one approach that works without wrapping every handler.
    private static readonly PropertyInfo? ProtectedCursorProperty =
        typeof(Microsoft.UI.Xaml.UIElement).GetProperty(
            "ProtectedCursor",
            BindingFlags.Instance | BindingFlags.NonPublic);

    partial void ApplyPlatformCursor()
    {
        if (Handler?.PlatformView is not Microsoft.UI.Xaml.UIElement element)
        {
            return;
        }

        try
        {
            ProtectedCursorProperty?.SetValue(
                element,
                Microsoft.UI.Input.InputSystemCursor.Create(
                    Microsoft.UI.Input.InputSystemCursorShape.SizeWestEast));
        }
        catch (Exception)
        {
            // Cursor shape is a nicety: a failed lookup must never break the resize itself.
        }
    }
}
