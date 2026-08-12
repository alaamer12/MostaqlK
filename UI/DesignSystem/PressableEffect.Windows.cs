using System.Reflection;

namespace MostaqlK.UI.DesignSystem;

public partial class PressableEffect
{
    private static readonly PropertyInfo? ProtectedCursorProperty =
        typeof(Microsoft.UI.Xaml.UIElement).GetProperty(
            "ProtectedCursor",
            BindingFlags.Instance | BindingFlags.NonPublic);

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
