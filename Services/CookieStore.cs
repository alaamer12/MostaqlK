using MostaqlK.Infrastructure.Database;
using MostaqlK.Infrastructure.Http;
using MostaqlK.Services.Diagnostics;

namespace MostaqlK.Services;

/// <summary>
/// Owns the user's Mostaql session cookie: validates an uploaded cookie file, stores it encrypted
/// in the local database (<see cref="ISecretRepository"/>), keeps the decrypted header in memory
/// for the pipeline, and installs itself as <see cref="CookieJar.SecureProvider"/> so
/// <c>MostaqlScraper</c>/<c>AssetDownloadService</c> pick it up without knowing where it came from.
/// <para>
/// A cookie is what turns an attachment from a "/register" stub into a real, downloadable file
/// URL, so this is a normal (if privileged) feature rather than an optional developer aid - hence
/// the Settings upload. The plaintext never touches disk: only the DPAPI-protected blob is stored.
/// </para>
/// </summary>
public sealed class CookieStore
{
    private readonly ISecretRepository _secrets;
    private string? _cookieHeader;

    public CookieStore(ISecretRepository secrets)
    {
        _secrets = secrets;
        CookieJar.SecureProvider = () => _cookieHeader;
    }

    /// <summary>Number of cookies currently held, or 0 when none is configured.</summary>
    public int CookieCount => _cookieHeader is null ? 0 : _cookieHeader.Split(';').Length;

    /// <summary>True when a session cookie is available to the pipeline right now.</summary>
    public bool HasCookie => _cookieHeader is { Length: > 0 };

    /// <summary>When the stored cookie was last uploaded (UTC), or <c>null</c> if never.</summary>
    public DateTime? UpdatedAtUtc { get; private set; }

    /// <summary>
    /// Loads the stored cookie into memory. Called once at startup, before the pipeline begins
    /// polling, so the very first fetch is already authenticated.
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var result = await _secrets.GetAsync(SecretRepository.MostaqlCookieKey, cancellationToken);
        if (result.IsError)
        {
            InteractionLogger.Failure(nameof(InitializeAsync), result.Error);
            return;
        }

        _cookieHeader = result.Value;

        var updated = await _secrets.GetUpdatedAtAsync(SecretRepository.MostaqlCookieKey, cancellationToken);
        UpdatedAtUtc = updated.IsError ? null : updated.Value;
    }

    /// <summary>
    /// Validates and stores the contents of a browser-exported cookie file. Returns the number of
    /// cookies stored, or <c>null</c> when the file contained nothing usable (so the caller can
    /// tell the user rather than silently storing an empty session).
    /// </summary>
    public async Task<int?> SaveFromFileContentAsync(string rawFileContent, CancellationToken cancellationToken = default)
    {
        var header = CookieJar.ParseFile(rawFileContent);
        if (header is null)
        {
            return null;
        }

        var result = await _secrets.SetAsync(SecretRepository.MostaqlCookieKey, header, cancellationToken);
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
    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        var result = await _secrets.DeleteAsync(SecretRepository.MostaqlCookieKey, cancellationToken);
        if (result.IsError)
        {
            InteractionLogger.Failure(nameof(ClearAsync), result.Error);
        }

        _cookieHeader = null;
        UpdatedAtUtc = null;
    }
}
