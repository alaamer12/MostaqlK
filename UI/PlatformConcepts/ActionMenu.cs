using Microsoft.Maui.Controls;
using MostaqlK.UI.PlatformComponents;

namespace MostaqlK.UI.PlatformConcepts;

/// <summary>
/// Action list surface. Structurally different per platform:
/// an action sheet on mobile vs a context menu / flyout on desktop.
/// </summary>
public static class ActionMenu
{
    public static readonly Func<View>? Current = PlatformSelect.For<Func<View>>(
        android: CreateMobileActionMenu,
        ios: CreateMobileActionMenu,
        windows: CreateContextMenu,
        macCatalyst: CreateContextMenu);

    /// <summary>
    /// Displays an action selection menu for the current platform.
    /// </summary>
    public static async Task<string?> ShowAsync(
        Page page, 
        string title, 
        string cancel, 
        string? destruction, 
        params string[] buttons)
    {
        return await page.DisplayActionSheetAsync(title, cancel, destruction, buttons);
    }

    private static View CreateContextMenu()
    {
        return new ContentView();
    }

    private static View CreateMobileActionMenu()
    {
        return new ContentView();
    }
}
