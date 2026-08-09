using Microsoft.Maui.Controls;
using MostaqlK.UI.PlatformComponents;

namespace MostaqlK.UI.PlatformConcepts;

/// <summary>
/// Primary navigation surface. Structurally different per platform, not just styled
/// differently: a bottom tab bar on mobile vs a side panel on desktop.
/// Windows (V1): stood in with a <see cref="Grid"/>-based side panel (two-column grid — nav
/// rail + content) since a dedicated docking control isn't part of stock MAUI controls.
/// </summary>
public static class NavigationControl
{
    public static readonly Func<View>? Current = PlatformSelect.For<Func<View>>(
        android: null, // TODO: BottomTabs — added only when V3 mobile work starts.
        ios: null, // TODO: BottomTabs — added only when V3 mobile work starts.
        windows: CreateSidePanel,
        macCatalyst: null); // TODO: SidePanel-equivalent — added only when V3 mobile work starts.

    private static View CreateSidePanel()
    {
        // Windows "SidePanel" stand-in: a Grid with a fixed-width nav rail column and a content
        // column. TODO: replace with the real sidebar (see projects.html mockup) once the
        // Design System / navigation items are wired up.
        return new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = GridLength.Star }
            }
        };
    }
}
