using System.Text;
using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace MostaqlK.Core.Utilities;

/// <summary>
/// Authoritative single ground for string normalization, Arabic orthographic folding,
/// digit conversion, diacritic stripping, and HTML cleaning.
/// </summary>
public static class StringNormalization
{
    private const string ArabicIndicDigits = "٠١٢٣٤٥٦٧٨٩";
    private const string ExtendedArabicIndicDigits = "۰۱۲۳۴۵۶۷۸۹";

    private static readonly Regex ArabicDiacriticsRegex = new(@"[\u064B-\u065F\u0670\u0640]", RegexOptions.Compiled);
    public static readonly char[] LabelTrimChars = [':', '\uFF1A', '\u061B', ';', '.', '\u060C', ',', '-', '\u2013', '\u2014', ' '];
    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);
    private static readonly Regex HtmlTagsRegex = new(@"<.*?>", RegexOptions.Compiled);

    /// <summary>Collapse runs of whitespace to a single space and trim.</summary>
    public static string Normalize(string? s) =>
        string.IsNullOrEmpty(s) ? string.Empty : WhitespaceRegex.Replace(s, " ").Trim();

    /// <summary>
    /// Converts Arabic-Indic (٠-٩) and extended/Persian Arabic-Indic (۰-۹) digits to ASCII (0-9).
    /// </summary>
    public static string ToAsciiDigits(string? s)
    {
        if (string.IsNullOrEmpty(s))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
        {
            var idx = ArabicIndicDigits.IndexOf(c);
            if (idx < 0)
            {
                idx = ExtendedArabicIndicDigits.IndexOf(c);
            }
            sb.Append(idx >= 0 ? (char)('0' + idx) : c);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Removes Arabic diacritics (tashkeel and tatweel).
    /// </summary>
    public static string StripDiacritics(string? s) =>
        string.IsNullOrEmpty(s) ? string.Empty : ArabicDiacriticsRegex.Replace(s, string.Empty);

    /// <summary>
    /// Canonical form used to compare Arabic labels regardless of orthographic variations,
    /// trailing colons/punctuation, diacritics, or alef/ya/ta-marbuta spellings.
    /// </summary>
    public static string NormalizeLabel(string? s)
    {
        var text = Normalize(s);
        if (text.Length == 0)
        {
            return string.Empty;
        }

        text = StripDiacritics(text);
        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            sb.Append(c switch
            {
                'أ' or 'إ' or 'آ' or 'ٱ' => 'ا',
                'ى' => 'ي',
                'ة' => 'ه',
                'ؤ' => 'و',
                'ئ' => 'ي',
                _ => c,
            });
        }

        return sb.ToString().Trim(LabelTrimChars);
    }

    /// <summary>
    /// De-entitizes HTML entities, removes markup tags, and trims quotes and whitespace.
    /// </summary>
    public static string CleanHtml(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        var text = HtmlEntity.DeEntitize(input).Trim();
        text = HtmlTagsRegex.Replace(text, string.Empty);
        return text.Trim('"', '\'', ' ', '\t', '\r', '\n');
    }
}
