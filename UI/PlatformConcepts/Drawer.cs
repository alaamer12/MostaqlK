using Microsoft.Maui.Controls;
using MostaqlK.UI.PlatformComponents;

namespace MostaqlK.UI.PlatformConcepts;

/// <summary>
/// Secondary/contextual panel. Structurally different per platform:
/// a bottom sheet / swipe-in drawer on mobile vs a side flyout on desktop.
/// </summary>
public static class Drawer
{
    public static readonly Func<View>? Current = PlatformSelect.For<Func<View>>(
        android: CreateMobileDrawer,
        ios: CreateMobileDrawer,
        windows: CreateFlyout,
        macCatalyst: CreateFlyout);

    /// <summary>
    /// Composes a main view with an overlay drawer container.
    /// </summary>
    public static View Build(View mainContent, View drawerContent, bool isOpen = false) => PlatformSelect.For<Func<View, View, bool, View>>(
        android: BuildMobileDrawer,
        ios: BuildMobileDrawer,
        windows: BuildDesktopFlyout,
        macCatalyst: BuildDesktopFlyout)?.Invoke(mainContent, drawerContent, isOpen) ?? mainContent;

    private static View CreateFlyout()
    {
        return new ContentView();
    }

    private static View CreateMobileDrawer()
    {
        return new ContentView();
    }

    private static View BuildDesktopFlyout(View mainContent, View drawerContent, bool isOpen)
    {
        var grid = new Grid();
        grid.Children.Add(mainContent);
        
        drawerContent.IsVisible = isOpen;
        drawerContent.HorizontalOptions = LayoutOptions.End;
        grid.Children.Add(drawerContent);
        return grid;
    }

    private static View BuildMobileDrawer(View mainContent, View drawerContent, bool isOpen)
    {
        var grid = new Grid();
        grid.Children.Add(mainContent);

        drawerContent.IsVisible = isOpen;
        drawerContent.VerticalOptions = LayoutOptions.End;
        grid.Children.Add(drawerContent);
        return grid;
    }
}
