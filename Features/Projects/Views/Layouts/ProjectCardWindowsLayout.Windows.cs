using System.Reflection;

namespace MostaqlK.Features.Projects.Views.Layouts;

/// <summary>
/// Windows half of the project-card row pointer affordance: the row opens details on tap, so it
/// carries the hand cursor like every other clickable surface. ProtectedCursor is the WinUI-3
/// mechanism (same reflection seam as <see cref="MostaqlK.UI.DesignSystem.PressableEffect"/> and
/// SplitterHandle); setting it on the row root inherits to the whole subtree, and clearing it to
/// null falls back to the default arrow.
/// </summary>
public partial class ProjectCardWindowsLayout
{
    private static readonly PropertyInfo? ProtectedCursorProperty =
        typeof(Microsoft.UI.Xaml.UIElement).GetProperty(
            "ProtectedCursor",
            BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly Microsoft.UI.Input.InputSystemCursor? HandCursor =
        Microsoft.UI.Input.InputSystemCursor.Create(Microsoft.UI.Input.InputSystemCursorShape.Hand);

    partial void SetRowCursorCore(bool hand)
    {
        if (ProjectCard_Root.Handler?.PlatformView is not Microsoft.UI.Xaml.UIElement element)
        {
            return;
        }

        try
        {
            ProtectedCursorProperty?.SetValue(element, hand ? HandCursor : null);
        }
        catch (Exception)
        {
            // Cursor shape is a nicety — never surface as a crash.
        }
    }
}
