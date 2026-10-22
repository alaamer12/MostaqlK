using Microsoft.Maui.Handlers;
using Microsoft.UI.Xaml.Controls;

namespace MostaqlK.UI.PlatformComponents;

/// <summary>
/// Windows-only tweaks for <see cref="AppToggle"/> (V1 scope).
/// </summary>
public partial class AppToggle
{
    /// <summary>
    /// WinUI's <see cref="ToggleSwitch"/> template reserves a ~154px label column for its On/Off
    /// content even when both are empty. The app's toggles are track-only, so that reserved column
    /// pushed the visible switch ~115px inward from the row's end (settings.html puts the track on
    /// the row edge). Clearing the content and the min width collapses the control to the track.
    /// </summary>
    static AppToggle()
    {
        SwitchHandler.Mapper.AppendToMapping("AppToggleCompact", (handler, view) =>
        {
            if (view is not AppToggle || handler.PlatformView is not ToggleSwitch toggle)
            {
                return;
            }

            toggle.OnContent = null;
            toggle.OffContent = null;
            toggle.MinWidth = 0;
        });
    }
}
