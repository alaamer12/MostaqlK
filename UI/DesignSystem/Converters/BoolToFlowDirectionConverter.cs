using System.Globalization;
using Microsoft.Maui.Controls;

namespace MostaqlK.UI.DesignSystem.Converters;

/// <summary>
/// Maps a "needs left-to-right isolation" flag onto a <see cref="FlowDirection"/> so an Arabic-first
/// page (RTL) can render a single Latin-currency cell (e.g. a nafezly "$" figure) in LTR without
/// flipping the whole page (backend-ui-contract §7). The flag is supplied by the schema renderer from
/// the descriptor's currency suffix — the converter itself is source-blind: <c>true ⇒ LeftToRight</c>,
/// anything else ⇒ <c>RightToLeft</c> (the page's own direction).
/// </summary>
public class BoolToFlowDirectionConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? FlowDirection.LeftToRight : FlowDirection.RightToLeft;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is FlowDirection.LeftToRight;
}
