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

    /// <summary>
    /// Composes the real navigation surface for the current platform out of a caller-supplied
    /// nav-rail view (the actual nav items/commands, wired by the call site — e.g.
    /// <c>MainWindowPage</c>'s sidebar buttons) and the page's main content. Windows (V1): the
    /// nav rail occupies a fixed-width column to the side of the content column, matching the
    /// projects.html sidebar layout.
    /// </summary>
    public static View Build(View navRail, View content) => PlatformSelect.For<Func<View, View, View>>(
        android: null, // TODO: BottomTabs — added only when V3 mobile work starts.
        ios: null,
        windows: BuildSidePanel,
        macCatalyst: null)?.Invoke(navRail, content) ?? content;

    private static View CreateSidePanel()
    {
        // Windows "SidePanel" stand-in: a Grid with a fixed-width nav rail column and a content
        // column.
        return new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = GridLength.Star }
            }
        };
    }

    private static View BuildSidePanel(View navRail, View content)
    {
        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(240) },
                new ColumnDefinition { Width = GridLength.Star }
            }
        };

        Grid.SetColumn(navRail, 0);
        Grid.SetColumn(content, 1);
        grid.Children.Add(navRail);
        grid.Children.Add(content);
        return grid;
    }
}
