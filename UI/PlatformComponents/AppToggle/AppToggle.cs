using Microsoft.Maui.Controls;
using MostaqlK.UI.DesignSystem;

namespace MostaqlK.UI.PlatformComponents;

/// <summary>
/// Shared, platform-neutral toggle switch (e.g. the settings dark-mode switch). Same shape
/// everywhere; only native handler mapping differs per OS (see the <c>AppToggle.Windows.cs</c>
/// partial).
/// </summary>
public partial class AppToggle : PressableSwitch
{
    public AppToggle()
    {
        // TODO: apply the shared "AppToggleBase" style resource once the Design System styles land.
    }
}
