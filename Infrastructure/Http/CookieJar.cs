using MostaqlK.Core.Sources;

namespace MostaqlK.Infrastructure.Http;

/// <summary>
/// One source's session cookie: resolves a <c>Cookie:</c> request-header value from the encrypted
/// store populated in Settings (and, in development builds only, from env vars / a repo-root
/// <c>cookies.txt</c>), filtered so a session can never be handed to a different source's request.
/// <para>
/// A cookie is what turns a project attachment from a "/register" stub into a real, downloadable
/// file URL, and the source renders anonymous visitors that stub on the <em>page</em> too - so the
/// header must attach to the page fetch as well as the file fetch.
/// </para>
/// There is one instance per registered source (see <see cref="ICookieJarProvider"/>); the
/// decrypted value itself lives in <see cref="CookieStore"/>, which installs it here via
/// <see cref="SecureProvider"/>.
/// </summary>
public interface ICookieJar
{
    /// <summary>The source this jar belongs to.</summary>
    public SourceId Source { get; }

    /// <summary>Describes where <see cref="Load"/> last found its cookies (for diagnostics/logging).</summary>
    public string? LastSource { get; }

    /// <summary>
    /// The primary, production source: the cookie the user uploaded in Settings, held encrypted in
    /// the local database and decrypted into memory by <see cref="Services.CookieStore"/>, which
    /// installs itself here at startup for its own source. A delegate so this MAUI-free type stays
    /// usable by the parser harness (which has no database).
    /// </summary>
    public Func<string?>? SecureProvider { get; set; }

    /// <summary>
    /// True when the env/cookie-file fallbacks are compiled in, i.e. this is a development build.
    /// The settings screen uses it to explain where a cookie came from when the user has not
    /// uploaded one.
    /// </summary>
    public bool DevelopmentFallbacksEnabled { get; }

    /// <summary>
    /// Resolves a cookie header, trying in order: the explicit <paramref name="explicitPath"/>,
    /// then the encrypted store (<see cref="SecureProvider"/>), and - in DEBUG builds only - the
    /// per-source env vars and a <c>cookies.txt</c> walked up from the current directory.
    /// Returns <c>null</c> when nothing usable is configured - callers treat that as "manual
    /// download required", not an error.
    /// </summary>
    public string? Load(string? explicitPath = null);

    /// <summary>
    /// Parses raw cookie-file text and filters it to this source's domain, so an uploaded session
    /// never carries another site's cookies. Used by <see cref="Services.CookieStore"/> before it
    /// persists a user's upload.
    /// </summary>
    public string? ParseForStorage(string rawFileContent);
}

/// <summary>
/// Per-source <see cref="ICookieJar"/>: delegates the format parsing to the pure
/// <see cref="CookieParser"/> and adds the source's domain filter, storage provider and
/// development fallbacks. The two accepted on-disk formats, the analytics-prefix ignore list, and
/// the quote-stripping behavior all live in <see cref="CookieParser"/> unchanged.
/// </summary>
public sealed class CookieJar : ICookieJar
{
    private readonly ISourceDescriptor _descriptor;

    /// <summary>Generic, source-derived inline env name, e.g. <c>MOSTAQLK_COOKIE_MOSTAQL</c>.</summary>
    private string GenericInlineEnvVar => "MOSTAQLK_COOKIE_" + _descriptor.Id.Value.ToUpperInvariant();

    /// <summary>Generic, source-derived cookie-file env name, e.g. <c>MOSTAQLK_COOKIE_MOSTAQL_FILE</c>.</summary>
    private string GenericFileEnvVar => GenericInlineEnvVar + "_FILE";

    public CookieJar(ISourceDescriptor descriptor)
    {
        _descriptor = descriptor;
    }

    public SourceId Source => _descriptor.Id;

    public string? LastSource { get; private set; }

    public Func<string?>? SecureProvider { get; set; }

    public bool DevelopmentFallbacksEnabled =>
#if DEBUG
        true;
#else
        false;
#endif

    public string? ParseForStorage(string rawFileContent) => CookieParser.ParseFile(rawFileContent, _descriptor.Domain);

    public string? Load(string? explicitPath = null)
    {
        LastSource = null;

        if (!string.IsNullOrWhiteSpace(explicitPath) && File.Exists(explicitPath))
        {
            LastSource = explicitPath;
            return ParseForStorage(File.ReadAllText(explicitPath));
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
        // executable cannot silently authenticate a real user's app. The primary env names are
        // derived from the source id; the descriptor's legacy names stay honored for backward
        // compatibility with the old developer workflow.
        foreach (var name in new[] { GenericInlineEnvVar, _descriptor.LegacyCookieEnvInlineVar })
        {
            var inline = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(inline))
            {
                LastSource = name + " (dev)";
                return ParseForStorage(inline);
            }
        }

        foreach (var name in new[] { GenericFileEnvVar, _descriptor.LegacyCookieEnvFileVar })
        {
            var envPath = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(envPath) && File.Exists(envPath))
            {
                LastSource = envPath + " (dev)";
                return ParseForStorage(File.ReadAllText(envPath));
            }
        }

        var discovered = CookieParser.FindUpwards("cookies.txt");
        if (discovered is not null)
        {
            LastSource = discovered + " (dev)";
            return ParseForStorage(File.ReadAllText(discovered));
        }
#endif

        return null;
    }
}
