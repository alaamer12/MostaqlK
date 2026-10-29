using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace MostaqlK.Core.Notifications;

/// <summary>
/// The visual chrome one toast is allowed to carry, already resolved to escaped URIs.
/// <para>
/// The record is deliberately tiny and has no color, font or size field, because that is the whole
/// constraint this feature works inside: Windows lets an app brand a notification through
/// <em>imagery</em> only — the toast surface color, its text color and the fonts belong to the shell
/// and the user's theme. Anything that looked like "paint the toast Mostaql blue" would be a promise
/// the platform cannot keep.
/// </para>
/// </summary>
/// <param name="LogoFileUri"><c>appLogoOverride</c> image, or null to keep the app's own logo.</param>
/// <param name="HeroFileUri"><c>hero</c> band — the one large surface a source's colors can occupy.</param>
/// <param name="CircleLogo">Whether Windows crops the logo to a circle.</param>
public sealed record ToastBrandVisual(string? LogoFileUri, string? HeroFileUri, bool CircleLogo = false)
{
    /// <summary>
    /// Today's chrome, and where every fallback lands: app logo only, no hero. Used when there is no
    /// descriptor, when brand art could not be produced, and for a genuinely multi-source batch — a
    /// toast must not wear one platform's colors for another platform's projects.
    /// </summary>
    public static ToastBrandVisual Neutral(string? baseLogoFileUri) => new(baseLogoFileUri, null, false);

    /// <summary>Nothing to add; a caller may skip the rewrite entirely.</summary>
    public bool IsEmpty => LogoFileUri is null && HeroFileUri is null;
}

/// <summary>
/// The one place a toast's branded markup is decided and serialized.
/// <para>
/// It is a pure string rewriter rather than logic inside each sender because the two Windows
/// notification APIs this app can pick (classic WinRT and the Windows App SDK) end up carrying the
/// <em>same</em> toast XML: <c>AppNotification.Payload</c> is that XML and can be rebuilt into an
/// <c>AppNotification</c>, which <c>WinAppSdkVariation</c> already exploits to inject the RTL lang
/// attribute. Deciding the visual once, in text space, is what stops the two backends growing
/// different definitions of "a Nafezly toast"; <c>ParserTests</c> asserts that equality directly.
/// </para>
/// <para>
/// Nothing here throws: an unexpected payload returns the input unchanged, because a cosmetic
/// failure must never cost the user the notification itself.
/// </para>
/// </summary>
public static class ToastBranding
{
    private static readonly Regex BindingOpen = new(
        "<binding\\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ImageElement = new(
        "<image\\b[^>]*/>|<image\\b[^>]*>\\s*</image>", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Local path → the percent-escaped <c>file:///</c> URI Windows reads for a desktop app's toast
    /// image (that protocol is allowed for desktop apps, not packaged ones).
    /// <para>
    /// Escaping is not decoration. The senders used to splice <c>file:///{path}</c> as text, so a path
    /// containing <c>#</c> — such as a dev checkout under <c>…\C#\MostaqlK\bin\…</c> — was read by the
    /// renderer as a fragment delimiter and the image silently disappeared.
    /// </para>
    /// <para>
    /// The path is also made absolute, for the same silent-failure reason: a relative cache directory
    /// produced <c>file:///scratch/toast/hero.png</c>, which the renderer could not resolve, so the
    /// whole brand band vanished and nothing on the toast explained why.
    /// </para>
    /// </summary>
    public static string? ToFileUri(string? localPath)
    {
        if (string.IsNullOrWhiteSpace(localPath))
        {
            return null;
        }

        if (localPath.StartsWith("file:", StringComparison.OrdinalIgnoreCase)
            || localPath.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            || localPath.StartsWith("ms-appx", StringComparison.OrdinalIgnoreCase)
            || localPath.StartsWith("ms-appdata", StringComparison.OrdinalIgnoreCase))
        {
            return localPath;
        }

        try
        {
            // Lexical only — this never touches the filesystem, so a not-yet-written file is fine.
            localPath = Path.GetFullPath(localPath);
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (IOException)
        {
            // A path whose root cannot be resolved (a disconnected drive) has no URI worth sending.
            return null;
        }

        var sb = new StringBuilder("file:///");
        foreach (var c in localPath)
        {
            if (c == '\\')
            {
                sb.Append('/');
            }
            else if (c is <= ' ' or >= '\u007f' or '"' or '<' or '>' or '#' or '%' or '{' or '}' or '|' or '^' or '`' or '[' or ']')
            {
                // Percent-encode by hand rather than trusting Uri's file-URI handling: '#' is exactly
                // the character that decides whether the image resolves or silently disappears, and
                // it is not worth a test suite's worth of doubt about what the Uri class will do with it.
                sb.Append(Uri.EscapeDataString(c.ToString()));
            }
            else
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }

    /// <summary>The <c>&lt;image&gt;</c> markup for <paramref name="visual"/>, or an empty string.</summary>
    public static string ImageElementsXml(ToastBrandVisual visual)
    {
        if (visual.IsEmpty)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();

        // Deliberately no whitespace between the elements: re-applying a visual strips the tags and
        // keeps whatever is around them, so a trailing newline here would accumulate on every pass
        // and "idempotent" would be a claim rather than a property.
        if (visual.HeroFileUri is not null)
        {
            sb.Append("<image placement='hero' src='").Append(visual.HeroFileUri).Append("' />");
        }

        if (visual.LogoFileUri is not null)
        {
            sb.Append("<image placement='appLogoOverride' src='").Append(visual.LogoFileUri);
            sb.Append(visual.CircleLogo ? "' hint-crop='circle' />" : "' />");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Replace the images inside the first <c>&lt;binding&gt;</c> of <paramref name="toastXml"/> with
    /// <paramref name="visual"/>'s, leaving every other element (text, actions, arguments) untouched.
    /// Idempotent: existing images are removed before the new ones are written.
    /// </summary>
    public static string ApplyToBindingXml(string toastXml, ToastBrandVisual visual)
    {
        if (string.IsNullOrEmpty(toastXml))
        {
            return toastXml;
        }

        try
        {
            var open = BindingOpen.Match(toastXml);
            if (!open.Success)
            {
                return toastXml; // no binding to brand; hand back the payload exactly as it arrived
            }

            var innerStart = open.Index + open.Length;
            var closeIndex = toastXml.IndexOf("</binding>", innerStart, StringComparison.OrdinalIgnoreCase);
            if (closeIndex < 0)
            {
                return toastXml; // self-closed or malformed: never risk producing broken XML
            }

            var stripped = ImageElement.Replace(toastXml[innerStart..closeIndex], string.Empty);
            var images = ImageElementsXml(visual);

            return string.Concat(
                toastXml.AsSpan(0, innerStart),
                images,
                stripped,
                toastXml.AsSpan(closeIndex));
        }
        catch (ArgumentOutOfRangeException)
        {
            return toastXml;
        }
    }

    /// <summary>XML escaping for a value placed inside an element — toast text is scraped user data.</summary>
    public static string EscapeXmlText(string? value) =>
        string.IsNullOrEmpty(value)
            ? string.Empty
            : value.Replace("&", "&amp;", StringComparison.Ordinal)
                   .Replace("<", "&lt;", StringComparison.Ordinal)
                   .Replace(">", "&gt;", StringComparison.Ordinal);
}
