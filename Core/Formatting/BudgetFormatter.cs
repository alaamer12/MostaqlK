using System.Globalization;
using System.Text.RegularExpressions;

namespace MostaqlK.Core.Formatting;

/// <summary>
/// Renders the raw <c>projects.budget</c> text (stored verbatim as scraped, e.g.
/// <c>"$250.00 - $500.00"</c>) in the presentation form the mockups specify:
/// <c>"2,500 - 5,500 {currency}"</c> — thousands separator, no decimal places, low value first,
/// then the currency suffix supplied by the project's own source descriptor (storage keeps
/// the source string untouched per the no-update policy; only the display layer normalises).
/// </summary>
public static partial class BudgetFormatter
{
    private const string EmptyPlaceholder = "—";

    [GeneratedRegex(@"\d[\d,]*(?:\.\d+)?", RegexOptions.CultureInvariant)]
    private static partial Regex AmountPattern();

    public static string Format(string? rawBudget, string currencySuffix)
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
            return WithSuffix(Amount(amounts[0]), currencySuffix);
        }

        var low = Math.Min(amounts[0], amounts[1]);
        var high = Math.Max(amounts[0], amounts[1]);
        return WithSuffix($"{Amount(low)} - {Amount(high)}", currencySuffix);
    }

    private static string WithSuffix(string amounts, string currencySuffix) =>
        string.IsNullOrEmpty(currencySuffix) ? amounts : $"{amounts} {currencySuffix}";

    private static string Amount(decimal value) => value.ToString("#,##0", CultureInfo.InvariantCulture);
}
