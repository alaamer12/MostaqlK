using Microsoft.Maui.Controls;
using MostaqlK.UI.PlatformComponents;

namespace MostaqlK.UI.PlatformConcepts;

/// <summary>
/// Action list surface. Structurally different per platform: an action sheet on mobile vs a
/// context menu on desktop.
/// Windows (V1): stood in with <see cref="MenuFlyout"/> (via <c>FlyoutBase.ContextFlyout</c>),
/// MAUI's built-in right-click context-menu control — the closest idiomatic match to a desktop
/// context menu (also used by the tray icon's own right-click menu).
/// </summary>
public static class ActionMenu
{
    public static readonly Func<View>? Current = PlatformSelect.For<Func<View>>(
        android: null, // TODO: ActionSheet — added only when V3 mobile work starts.
        ios: null, // TODO: ActionSheet — added only when V3 mobile work starts.
        windows: CreateContextMenu,
        macCatalyst: null); // TODO: ContextMenu-equivalent — added only when V3 mobile work starts.

    private static View CreateContextMenu()
    {
        // Windows "ContextMenu" stand-in: a lightweight content container standing in for the
        // element that would carry a MenuFlyout via FlyoutBase.ContextFlyout. TODO: attach a
        // real MenuFlyout with MenuFlyoutItem entries once a concrete action-list use case
        // (e.g. project card right-click actions) is implemented.
        return new ContentView();
    }
}
