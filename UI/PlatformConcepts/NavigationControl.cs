using Microsoft.Maui.Controls;
using MostaqlK.UI.PlatformComponents;

namespace MostaqlK.UI.PlatformConcepts;

/// <summary>
/// Primary navigation surface. Structurally different per platform:
/// a bottom tab / bar on mobile vs a side panel on desktop.
/// </summary>
public static class NavigationControl
{
    public static readonly Func<View>? Current = PlatformSelect.For<Func<View>>(
        android: CreateBottomTabs,
        ios: CreateBottomTabs,
        windows: CreateSidePanel,
        macCatalyst: CreateSidePanel);

    /// <summary>
    /// Composes the navigation surface for the current platform:
    /// Desktop: 2-column side rail + content.
    /// Mobile: 2-row content + bottom navigation bar.
    /// </summary>
    public static View Build(View navBar, View content) => PlatformSelect.For<Func<View, View, View>>(
        android: BuildBottomNav,
        ios: BuildBottomNav,
        windows: BuildSidePanel,
        macCatalyst: BuildSidePanel)?.Invoke(navBar, content) ?? content;

    private static View CreateSidePanel()
    {
        return new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = GridLength.Star }
            }
        };
    }

    private static View CreateBottomTabs()
    {
        return new Grid
        {
            RowDefinitions =
            {
                new RowDefinition { Height = GridLength.Star },
                new RowDefinition { Height = GridLength.Auto }
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

    private static View BuildBottomNav(View navBar, View content)
    {
        var grid = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition { Height = GridLength.Star },
                new RowDefinition { Height = GridLength.Auto }
            }
        };

        Grid.SetRow(content, 0);
        Grid.SetRow(navBar, 1);
        grid.Children.Add(content);
        grid.Children.Add(navBar);
        return grid;
    }
}
