using System.Text;

namespace MostaqlK.Infrastructure.Http;

/// <summary>
/// Pure, MAUI-free cookie-file parsing: turns browser-exported text into a single
/// <c>Cookie:</c> request-header value. Deliberately has no dependency on the source contracts
/// so it can be linked straight into the <c>tools/ParserTests</c> harness (which has no DI
/// container and no <c>MostaqlK.Core.Sources</c>) while the per-source <see cref="CookieJar"/>
/// adds domain filtering and storage on top of it.
/// <para>
/// Two on-disk shapes are accepted, because that is what real exports look like:
/// <list type="bullet">
/// <item>Netscape/curl format (<c># Netscape HTTP Cookie File</c>, 7 tab-separated columns) - the
/// output of "cookies.txt" browser extensions and <c>curl -c</c>. The first column is the cookie's
/// domain; when a <c>domain</c> filter is supplied these rows are matched against it
/// so one source's session can never be handed to another source's request.</item>
/// <item>A plain <c>name=value</c> (optionally quoted) per line, or one long
/// <c>a=1; b=2</c> header string - what you get by copying the Cookie header out of DevTools.
/// These carry no domain column, so a domain filter leaves them untouched (they are always
/// supplied per-source by construction).</item>
/// </list>
/// </para>
/// This type holds no secret of its own: the caller supplies the raw text.
/// </summary>
public static class CookieParser
{
    /// <summary>Names that are pure analytics/noise and only make the header longer.</summary>
    private static readonly string[] IgnoredCookiePrefixes = ["_ga", "_gid", "_gat", "__stripe", "_fbp", "_gcl"];

    /// <summary>
    /// Parses raw cookie-file text into a <c>Cookie:</c> header value with no domain filtering.
    /// The parse-only entry point the test harness and the pre-storage validation path use when a
    /// single source is all that exists. Public so it can be unit tested.
    /// </summary>
    public static string? ParseFile(string raw) => ParseFile(raw, domain: null);

    /// <summary>
    /// Parses raw cookie-file text, keeping only cookies that belong to <paramref name="domain"/>
    /// when a Netscape domain column is present (subdomain-tolerant). Pass <c>null</c> to keep
    /// every row regardless of domain.
    /// </summary>
    public static string? ParseFile(string raw, string? domain)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        // Preserve insertion order but keep only the last value seen for a given name, which is
        // how a browser would resolve duplicate entries from a re-login.
        var jar = new Dictionary<string, string>(StringComparer.Ordinal);
        var order = new List<string>();

        foreach (var line in raw.Split('\n'))
        {
            var text = line.Trim().TrimEnd('\r');
            if (text.Length == 0 || text.StartsWith('#'))
            {
                continue;
            }

            foreach (var (name, value) in ParseLine(text, domain))
            {
                if (name.Length == 0 || IgnoredCookiePrefixes.Any(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                if (!jar.ContainsKey(name))
                {
                    order.Add(name);
                }
                jar[name] = value;
            }
        }

        if (order.Count == 0)
        {
            return null;
        }

        var sb = new StringBuilder();
        foreach (var name in order)
        {
            if (sb.Length > 0)
            {
                sb.Append("; ");
            }
            sb.Append(name).Append('=').Append(jar[name]);
        }
        return sb.ToString();
    }

    private static IEnumerable<(string Name, string Value)> ParseLine(string text, string? domain)
    {
        // Netscape format: domain \t includeSubdomains \t path \t secure \t expiry \t name \t value
        var columns = text.Split('\t');
        if (columns.Length >= 7)
        {
            // The domain column was previously discarded. Filtering on it is what stops a shared
            // cookie file from leaking a session to the wrong source once more than one exists.
            if (domain is null || DomainMatches(columns[0].Trim(), domain))
            {
                yield return (columns[5].Trim(), Unquote(columns[6].Trim()));
            }
            yield break;
        }

        // Otherwise: one or more "name=value" pairs, semicolon separated.
        foreach (var pair in text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var idx = pair.IndexOf('=');
            if (idx <= 0)
            {
                continue;
            }
            yield return (pair[..idx].Trim(), Unquote(pair[(idx + 1)..].Trim()));
        }
    }

    /// <summary>
    /// True when a Netscape domain column belongs to <paramref name="sourceDomain"/>: exact match,
    /// the column is a subdomain of the source host, or the column is a parent domain covering the
    /// source host. Leading dots (Netscape's "include subdomains" marker) are ignored.
    /// </summary>
    private static bool DomainMatches(string cookieDomain, string sourceDomain)
    {
        var cookie = cookieDomain.Trim().TrimStart('.').ToLowerInvariant();
        var source = sourceDomain.Trim().TrimStart('.').ToLowerInvariant();

        if (cookie.Length == 0 || source.Length == 0)
        {
            return false;
        }

        return cookie == source
            || cookie.EndsWith("." + source, StringComparison.Ordinal)
            || source.EndsWith("." + cookie, StringComparison.Ordinal);
    }

    /// <summary>
    /// Strips the surrounding double quotes some exporters add. They must not survive into the
    /// header: Mostaql's Laravel session/XSRF values are already URL-encoded, and sending them
    /// quoted makes the server decode a different string and reject the session.
    /// </summary>
    private static string Unquote(string value) =>
        value.Length >= 2 && value[0] == '"' && value[^1] == '"' ? value[1..^1] : value;

    /// <summary>
    /// Walks up from the current directory looking for <paramref name="fileName"/>. Development
    /// builds only, so a shipped app can never pick a session up off the filesystem.
    /// </summary>
    public static string? FindUpwards(string fileName)
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
            dir = dir.Parent;
        }
        return null;
    }
}
