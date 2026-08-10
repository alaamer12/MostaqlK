using System.Globalization;
using System.Text.RegularExpressions;

namespace MostaqlK.Core.Formatting;

/// <summary>
/// Renders the raw <c>projects.budget</c> text (stored verbatim as scraped, e.g.
/// <c>"$250.00 - $500.00"</c>) in the presentation form the mockups specify:
/// <c>"2,500 - 5,500 ر.س"</c> — thousands separator, no decimal places, low value first,
/// Saudi Riyal suffix. Storage keeps the source string untouched (no-update policy); only
/// the display layer normalises it.
/// </summary>
public static partial class BudgetFormatter
{
    /// <summary>Currency suffix used across the mockups (projects.html / project-details.html).</summary>
    public const string CurrencySuffix = "ر.س";

    private const string EmptyPlaceholder = "—";

    [GeneratedRegex(@"\d[\d,]*(?:\.\d+)?", RegexOptions.CultureInvariant)]
    private static partial Regex AmountPattern();

    public static string Format(string? rawBudget)
    {
        if (string.IsNullOrWhiteSpace(rawBudget))
        {
            return EmptyPlaceholder;
        }

        var amounts = new List<decimal>();
        foreach (Match match in AmountPattern().Matches(rawBudget))
        {
            var normalized = match.Value.Replace(",", string.Empty);
            if (decimal.TryParse(normalized, NumberStyles.Any, CultureInfo.InvariantCulture, out var value))
            {
                amounts.Add(value);
            }
        }

        if (amounts.Count == 0)
        {
            return rawBudget.Trim();
        }

        if (amounts.Count == 1)
        {
            return $"{Amount(amounts[0])} {CurrencySuffix}";
        }

        var low = Math.Min(amounts[0], amounts[1]);
        var high = Math.Max(amounts[0], amounts[1]);
        return $"{Amount(low)} - {Amount(high)} {CurrencySuffix}";
    }

    private static string Amount(decimal value) => value.ToString("#,##0", CultureInfo.InvariantCulture);
}
