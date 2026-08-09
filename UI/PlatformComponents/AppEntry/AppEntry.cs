using Microsoft.Maui.Controls;

namespace MostaqlK.UI.PlatformComponents;

/// <summary>
/// Shared, platform-neutral text entry field. Same shape everywhere; only native handler
/// mapping differs per OS (see the <c>AppEntry.Windows.cs</c> partial).
/// </summary>
public partial class AppEntry : Entry
{
    public AppEntry()
    {
        // TODO: apply the shared "AppEntryBase" style resource once the Design System styles land.
    }
}
