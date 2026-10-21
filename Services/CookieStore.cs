using MostaqlK.Core.Sources;
using MostaqlK.Infrastructure.Database;
using MostaqlK.Infrastructure.Http;
using MostaqlK.Services.Diagnostics;

namespace MostaqlK.Services;

/// <summary>
/// Owns one source's session cookie: validates an uploaded cookie file, stores it encrypted in the
/// local database (<see cref="ISecretRepository"/>) under the descriptor's <c>CookieSecretKey</c>,
/// keeps the decrypted header in memory, and installs itself into that source's
/// <see cref="ICookieJar"/> so the fetcher/asset downloader pick it up without knowing where it
/// came from. Sessions stay isolated per source — a nafezly jar never sees the Mostaql header.
/// <para>
/// A cookie is what turns an attachment from a "/register" stub into a real, downloadable file URL,
/// so this is a normal (if privileged) feature rather than an optional developer aid - hence the
/// Settings upload. The plaintext never touches disk: only the DPAPI-protected blob is stored.
/// </para>
/// <para>
/// Legacy migration is seamless: if the new namespaced key is empty but the pre-namespacing row
/// (<c>descriptor.LegacyCookieSecretKey</c>) still holds a value, it is loaded, rewritten under the
/// new key, and the old row removed — so an existing user's uploaded session keeps working with no
/// visible change and no manual re-upload.
/// </para>
/// </summary>
public sealed class CookieStore
{
    private readonly ISecretRepository _secrets;
    private readonly ISourceDescriptor _descriptor;
    private readonly ICookieJar _cookieJar;
    private string? _cookieHeader;

    public CookieStore(ISecretRepository secrets, ISourceDescriptor descriptor, ICookieJarProvider cookieJars)
    {
        _secrets = secrets;
        _descriptor = descriptor;
        _cookieJar = cookieJars.For(descriptor.Id);
        _cookieJar.SecureProvider = () => _cookieHeader;
    }

    /// <summary>Number of cookies currently held, or 0 when none is configured.</summary>
    public int CookieCount => _cookieHeader is null ? 0 : _cookieHeader.Split(';').Length;

    /// <summary>True when a session cookie is available to the pipeline right now.</summary>
    public bool HasCookie => _cookieHeader is { Length: > 0 };

    /// <summary>True when this source's jar still accepts development cookie/env fallbacks.</summary>
    public bool DevelopmentFallbacksEnabled => _cookieJar.DevelopmentFallbacksEnabled;

    /// <summary>When the stored cookie was last uploaded (UTC), or <c>null</c> if never.</summary>
    public DateTime? UpdatedAtUtc { get; private set; }

    /// <summary>
    /// Loads the stored cookie into memory. Called once at startup, before the pipeline begins
    /// polling, so the very first fetch is already authenticated. Rewrites a legacy-keyed session
    /// under the namespaced key the first time it is seen.
    /// </summary>
    [Core.ErrorOutcome(Core.ErrorOutcome.Handled, Label = "Secret read/migration failures logged, leave the store anonymous")]
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var result = await _secrets.GetAsync(_descriptor.CookieSecretKey, cancellationToken);
        if (result.IsError)
        {
            InteractionLogger.Failure(nameof(InitializeAsync), result.Error);
            return;
        }

        _cookieHeader = result.Value;

        var updated = await _secrets.GetUpdatedAtAsync(_descriptor.CookieSecretKey, cancellationToken);
        UpdatedAtUtc = updated.IsError ? null : updated.Value;

        // Seamless one-time migration from the pre-namespacing key.
        if (string.IsNullOrEmpty(_cookieHeader) && !string.IsNullOrEmpty(_descriptor.LegacyCookieSecretKey))
        {
            var legacy = await _secrets.GetAsync(_descriptor.LegacyCookieSecretKey, cancellationToken);
            if (legacy.IsOk && !string.IsNullOrEmpty(legacy.Value))
            {
                // Capture the original upload timestamp before the rewrite resets it, then move the
                // value to the namespaced key and drop the legacy row.
                var legacyUpdated = await _secrets.GetUpdatedAtAsync(_descriptor.LegacyCookieSecretKey, cancellationToken);
                _cookieHeader = legacy.Value;
                UpdatedAtUtc = legacyUpdated.IsError ? null : legacyUpdated.Value;

                var rewrite = await _secrets.SetAsync(_descriptor.CookieSecretKey, legacy.Value, cancellationToken);
                if (rewrite.IsOk)
                {
                    await _secrets.DeleteAsync(_descriptor.LegacyCookieSecretKey, cancellationToken);
                }
            }
        }
    }

    /// <summary>
    /// Validates and stores the contents of a browser-exported cookie file, filtered to this
    /// source's domain. Returns the number of cookies stored, or <c>null</c> when the file contained
    /// nothing usable (so the caller can tell the user rather than silently storing an empty session).
    /// </summary>
    [Core.ErrorOutcome(Core.ErrorOutcome.Handled, Label = "Secret write failure logged, nothing stored")]
    public async Task<int?> SaveFromFileContentAsync(string rawFileContent, CancellationToken cancellationToken = default)
    {
        var header = _cookieJar.ParseForStorage(rawFileContent);
        if (header is null)
        {
            return null;
        }

        var result = await _secrets.SetAsync(_descriptor.CookieSecretKey, header, cancellationToken);
        if (result.IsError)
        {
            InteractionLogger.Failure(nameof(SaveFromFileContentAsync), result.Error);
            return null;
        }

        _cookieHeader = header;
        UpdatedAtUtc = DateTime.UtcNow;
        return CookieCount;
    }

    /// <summary>Forgets the stored cookie, both in memory and in the database.</summary>
    [Core.ErrorOutcome(Core.ErrorOutcome.Handled, Label = "Secret delete failure logged, memory still cleared")]
    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        var result = await _secrets.DeleteAsync(_descriptor.CookieSecretKey, cancellationToken);
        if (result.IsError)
        {
            InteractionLogger.Failure(nameof(ClearAsync), result.Error);
        }

        _cookieHeader = null;
        UpdatedAtUtc = null;
    }
}
