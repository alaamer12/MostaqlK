using System.Text;

namespace MostaqlK.Infrastructure.Http;

/// <summary>
/// Turns a browser-exported cookie file into a single <c>Cookie:</c> request-header value.
/// <para>
/// Mostaql serves project attachments only to a logged-in session, and the project page itself
/// renders anonymous visitors a <c>/register?...</c> stub in place of the real file URL - so the
/// cookie has to be attached to the <em>page</em> fetch, not just the file fetch.
/// </para>
/// <para>
/// Two on-disk shapes are accepted, because that is what real exports look like:
/// <list type="bullet">
/// <item>Netscape/curl format (<c># Netscape HTTP Cookie File</c>, 7 tab-separated columns) - the
/// output of "cookies.txt" browser extensions and <c>curl -c</c>.</item>
/// <item>A plain <c>name=value</c> (optionally quoted) per line, or one long
/// <c>a=1; b=2</c> header string - what you get by copying the Cookie header out of DevTools.</item>
/// </list>
/// </para>
/// This type deliberately contains no secret of its own: the caller supplies the path, or it is
/// discovered from <c>MOSTAQL_COOKIE_FILE</c> / a repo-root <c>cookies.txt</c> during development.
/// </summary>
public static class CookieJar
{
    /// <summary>Names that are pure analytics/noise and only make the header longer.</summary>
    private static readonly string[] IgnoredCookiePrefixes = ["_ga", "_gid", "_gat", "__stripe", "_fbp", "_gcl"];

    /// <summary>Describes where <see cref="Load"/> last found its cookies (for diagnostics/logging).</summary>
    public static string? LastSource { get; private set; }

    /// <summary>
    /// The primary, production source: the cookie the user uploaded in Settings, held encrypted in
    /// the local database and decrypted into memory by <c>CookieStore</c>, which installs itself
    /// here at startup. Kept as a delegate so this MAUI-free type stays usable by the parser test
    /// harness (which has no database) and does not depend on the app's DI container.
    /// </summary>
    public static Func<string?>? SecureProvider { get; set; }

    /// <summary>
    /// Resolves a cookie header, trying in order: the explicit <paramref name="explicitPath"/>,
    /// then the encrypted store populated from Settings (<see cref="SecureProvider"/>), and - in
    /// DEBUG builds only - the <c>MOSTAQL_COOKIE</c>/<c>MOSTAQL_COOKIE_FILE</c> env vars and a
    /// <c>cookies.txt</c> walked up from the current directory.
    /// Returns <c>null</c> when nothing usable is configured - callers must treat that as
    /// "manual download required" rather than as an error.
    /// </summary>
    public static string? Load(string? explicitPath = null)
    {
        LastSource = null;

        if (!string.IsNullOrWhiteSpace(explicitPath) && File.Exists(explicitPath))
        {
            LastSource = explicitPath;
            return ParseFile(File.ReadAllText(explicitPath));
        }

        if (SecureProvider?.Invoke() is { Length: > 0 } stored)
        {
            LastSource = "encrypted store";
            return stored;
        }

#if DEBUG
        // Development-only conveniences. A shipped build must never pick a session up off the
        // filesystem or the environment: in Release the only accepted source is the encrypted
        // store above (populated from Settings), so a stray plaintext `cookies.txt` next to the
        // executable cannot silently authenticate a real user's app.
        var inline = Environment.GetEnvironmentVariable("MOSTAQL_COOKIE");
        if (!string.IsNullOrWhiteSpace(inline))
        {
            LastSource = "MOSTAQL_COOKIE (dev)";
            return ParseFile(inline);
        }

        var envPath = Environment.GetEnvironmentVariable("MOSTAQL_COOKIE_FILE");
        if (!string.IsNullOrWhiteSpace(envPath) && File.Exists(envPath))
        {
            LastSource = envPath + " (dev)";
            return ParseFile(File.ReadAllText(envPath));
        }

        var discovered = FindUpwards("cookies.txt");
        if (discovered is not null)
        {
            LastSource = discovered + " (dev)";
            return ParseFile(File.ReadAllText(discovered));
        }
#endif

        return null;
    }

    /// <summary>
    /// True when the repo-root/env cookie fallbacks are compiled in, i.e. this is a development
    /// build. The settings screen uses it to explain where a cookie came from when the user has
    /// not uploaded one.
    /// </summary>
    public static bool DevelopmentFallbacksEnabled =>
#if DEBUG
        true;
#else
        false;
#endif

    /// <summary>
    /// Parses raw cookie-file text into a <c>Cookie:</c> header value. Public so it can be unit
    /// tested and so the future settings screen can validate a user-uploaded file before storing it.
    /// </summary>
    public static string? ParseFile(string raw)
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

            foreach (var (name, value) in ParseLine(text))
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

    private static IEnumerable<(string Name, string Value)> ParseLine(string text)
    {
        // Netscape format: domain \t includeSubdomains \t path \t secure \t expiry \t name \t value
        var columns = text.Split('\t');
        if (columns.Length >= 7)
        {
            yield return (columns[5].Trim(), Unquote(columns[6].Trim()));
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
    /// Strips the surrounding double quotes some exporters add. They must not survive into the
    /// header: Mostaql's Laravel session/XSRF values are already URL-encoded, and sending them
    /// quoted makes the server decode a different string and reject the session.
    /// </summary>
    private static string Unquote(string value) =>
        value.Length >= 2 && value[0] == '"' && value[^1] == '"' ? value[1..^1] : value;

#if DEBUG
    private static string? FindUpwards(string fileName)
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
#endif
}
