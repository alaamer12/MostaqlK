using Microsoft.Maui.Controls;
using MostaqlK.UI.PlatformComponents;

namespace MostaqlK.UI.PlatformConcepts;

/// <summary>
/// Secondary/contextual panel. Structurally different per platform: a swipe-in drawer on
/// mobile vs a flyout on desktop.
/// Windows (V1): stood in with <see cref="FlyoutPage"/>, MAUI's built-in flyout-shell control —
/// the closest idiomatic match to a desktop flyout panel.
/// </summary>
public static class Drawer
{
    public static readonly Func<View>? Current = PlatformSelect.For<Func<View>>(
        android: null, // TODO: SwipeDrawer — added only when V3 mobile work starts.
        ios: null, // TODO: SwipeDrawer — added only when V3 mobile work starts.
        windows: CreateFlyout,
        macCatalyst: null); // TODO: Flyout-equivalent — added only when V3 mobile work starts.

    private static View CreateFlyout()
    {
        // Windows "Flyout" stand-in: a lightweight content container representing the flyout
        // panel's content slot. TODO: wire this up to an actual FlyoutPage.Flyout / Shell
        // flyout once a concrete secondary-panel use case is implemented.
        return new ContentView();
    }
}
