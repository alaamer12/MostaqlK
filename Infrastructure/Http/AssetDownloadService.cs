using MostaqlK.Core;
using MostaqlK.Models;

namespace MostaqlK.Infrastructure.Http;

/// <summary>
/// Outcome status for resolving a single <see cref="Asset"/>. Mirrors the Python prototype's
/// <c>STATUS_*</c> constants in <c>attachment_downloader.py</c> exactly.
/// </summary>
public enum AttachmentStatus
{
    /// <summary>Public link, no auth needed — just use the direct URL.</summary>
    ReadyUrl,

    /// <summary>Fetched to disk successfully.</summary>
    Downloaded,

    /// <summary>Needs a human: requires auth and no cookie is configured.</summary>
    ManualDownloadRequired,

    /// <summary>A cookie was provided but the authenticated request was rejected (or errored).</summary>
    AuthFailed,
}

/// <summary>Resolved outcome of <see cref="AssetDownloadService.ResolveAsync"/> for a single asset.</summary>
/// <param name="Status">Which of the four outcomes was reached.</param>
/// <param name="Url">The direct URL to use, when <see cref="Status"/> is <see cref="AttachmentStatus.ReadyUrl"/>.</param>
/// <param name="LocalPath">The path the file was saved to, when <see cref="Status"/> is <see cref="AttachmentStatus.Downloaded"/>.</param>
/// <param name="Message">Human-readable explanation, populated for the manual/auth-failed paths (mirrors the Python "message" field).</param>
public sealed record AttachmentResolution(AttachmentStatus Status, string? Url, string? LocalPath, string? Message);

/// <summary>
/// Resolves a project <see cref="Asset"/> to a definitive download outcome, mirroring
/// <c>.repertoire/progress/python/parser/scratch/attachment_downloader.py</c>'s
/// <c>resolve_attachment()</c> exactly:
/// <list type="bullet">
/// <item>not <see cref="Asset.RequiresAuth"/> → <see cref="AttachmentStatus.ReadyUrl"/> (use <see cref="Asset.Url"/> directly).</item>
/// <item><see cref="Asset.RequiresAuth"/> + no cookie configured → <see cref="AttachmentStatus.ManualDownloadRequired"/>.</item>
/// <item><see cref="Asset.RequiresAuth"/> + cookie configured → authenticated GET; HTML response (rejected/expired
/// session) or a network failure → <see cref="AttachmentStatus.AuthFailed"/>, otherwise the file is saved and
/// <see cref="AttachmentStatus.Downloaded"/> is returned.</item>
/// </list>
/// Never implements a login flow and never hardcodes a secret — the cookie is read purely from
/// <c>MOSTAQL_COOKIE</c> / <c>MOSTAQL_COOKIE_FILE</c>, matching the Python original.
/// </summary>
public sealed class AssetDownloadService
{
    private static readonly byte[][] HtmlSniffMarkers =
    [
        "<!DOCTYPE html"u8.ToArray(),
        "<html"u8.ToArray(),
    ];

    private readonly HttpClient _httpClient;

    public AssetDownloadService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    [ErrorOutcome(ErrorOutcome.Handled, Label = "Auth/network failure downloading the asset surfaced as AttachmentStatus.AuthFailed")]
    public async Task<AttachmentResolution> ResolveAsync(Asset asset, CancellationToken cancellationToken = default)
    {
        if (!asset.RequiresAuth)
        {
            return new AttachmentResolution(AttachmentStatus.ReadyUrl, asset.Url, null, null);
        }

        if (string.IsNullOrWhiteSpace(asset.RawUrl))
        {
            return new AttachmentResolution(
                AttachmentStatus.ManualDownloadRequired,
                null,
                null,
                $"No URL captured for '{asset.FileName}'; nothing to download.");
        }

        var cookieHeader = GetConfiguredCookieHeader();
        if (string.IsNullOrWhiteSpace(cookieHeader))
        {
            return new AttachmentResolution(
                AttachmentStatus.ManualDownloadRequired,
                null,
                null,
                $"'{asset.FileName}' requires a logged-in Mostaql session to download. " +
                "No MOSTAQL_COOKIE/MOSTAQL_COOKIE_FILE configured, so it was NOT fetched automatically. " +
                $"Please open this link in your own logged-in browser and download it manually: {asset.RawUrl}");
        }

        byte[] data;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, asset.RawUrl);
            request.Headers.TryAddWithoutValidation("Cookie", cookieHeader);
            request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0");

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            data = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return new AttachmentResolution(
                AttachmentStatus.AuthFailed,
                null,
                null,
                $"Download attempt for '{asset.FileName}' failed: {ex.Message}. Manual link: {asset.RawUrl}");
        }

        if (LooksLikeHtml(data))
        {
            return new AttachmentResolution(
                AttachmentStatus.AuthFailed,
                null,
                null,
                $"Configured cookie was not accepted for '{asset.FileName}' (received an HTML page instead of a " +
                $"file — likely an expired/invalid session). Manual link: {asset.RawUrl}");
        }

        var destDir = Path.Combine(FileSystem.CacheDirectory, "attachments");
        Directory.CreateDirectory(destDir);
        var localPath = Path.Combine(destDir, asset.FileName);
        await File.WriteAllBytesAsync(localPath, data, cancellationToken);

        return new AttachmentResolution(AttachmentStatus.Downloaded, null, localPath, null);
    }

    /// <summary>
    /// Reads the optional auth cookie from configuration (env var, or a file path stored in an
    /// env var) — never from a hardcoded value in code. Mirrors
    /// <c>get_configured_cookie_header()</c> in the Python prototype exactly.
    /// </summary>
    private static string? GetConfiguredCookieHeader()
    {
        var cookie = Environment.GetEnvironmentVariable("MOSTAQL_COOKIE");
        if (!string.IsNullOrWhiteSpace(cookie))
        {
            return JoinNonEmptyLines(cookie);
        }

        var cookieFile = Environment.GetEnvironmentVariable("MOSTAQL_COOKIE_FILE");
        if (!string.IsNullOrWhiteSpace(cookieFile) && File.Exists(cookieFile))
        {
            return JoinNonEmptyLines(File.ReadAllText(cookieFile));
        }

        return null;
    }

    private static string JoinNonEmptyLines(string raw)
    {
        var lines = raw.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => line.Length > 0);
        return string.Join("; ", lines);
    }

    private static bool LooksLikeHtml(byte[] data)
    {
        var head = data.AsSpan(0, Math.Min(512, data.Length));
        foreach (var marker in HtmlSniffMarkers)
        {
            if (head.IndexOf(marker.AsSpan()) >= 0)
            {
                return true;
            }
        }

        return false;
    }
}
