using Microsoft.Maui.Controls;

namespace MostaqlK.UI.PlatformComponents;

/// <summary>
/// Shared, platform-neutral button. Looks and behaves the same on every target — only native
/// handler mapping / padding / corner-radius tweaks differ per OS (see the
/// <c>AppButton.Windows.cs</c> partial). Style tokens are applied via
/// <c>Resources/Styles/AppButtonStyle.xaml</c> and its per-platform <c>BasedOn</c> override.
/// </summary>
public partial class AppButton : Button
{
    public AppButton()
    {
        // TODO: apply the shared "AppButtonBase" style resource once the Design System styles land.
    }
}
