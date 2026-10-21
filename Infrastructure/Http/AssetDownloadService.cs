using MostaqlK.Core;
using MostaqlK.Core.Platform;
using MostaqlK.Core.Sources;
using MostaqlK.Models;
using MostaqlK.Services.Diagnostics;

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
/// Never implements a login flow and never hardcodes a secret — the caller names the source, whose
/// <see cref="ICookieJar"/> and <see cref="ISourceDescriptor"/> supply the session and every
/// site-specific string. Nothing here names a website: the same code serves any registered source.
/// </summary>
public sealed class AssetDownloadService
{
    private static readonly byte[][] HtmlSniffMarkers =
    [
        "<!DOCTYPE html"u8.ToArray(),
        "<html"u8.ToArray(),
    ];

    private readonly HttpClient _httpClient;
    private readonly SourceRegistry _registry;
    private readonly ICookieJarProvider _cookieJars;

    public AssetDownloadService(HttpClient httpClient, SourceRegistry registry, ICookieJarProvider cookieJars)
    {
        _httpClient = httpClient;
        _registry = registry;
        _cookieJars = cookieJars;
    }

    [ErrorOutcome(ErrorOutcome.Handled, Label = "Auth/network failure downloading the asset surfaced as AttachmentStatus.AuthFailed")]
    public async Task<AttachmentResolution> ResolveAsync(SourceId source, Asset asset, CancellationToken cancellationToken = default)
    {
        var descriptor = _registry.Get(source);
        var cookieJar = _cookieJars.For(source);

        var cookieHeader = cookieJar.Load();
        InteractionLogger.Mark("AssetDownloadService.CookieSource", "A", new { Source = cookieJar.LastSource, HasCookie = !string.IsNullOrEmpty(cookieHeader) });

        // If it's explicitly marked as requiring auth but we have no cookie, we can't even try.
        if (asset.RequiresAuth && string.IsNullOrWhiteSpace(cookieHeader))
        {
            if (string.IsNullOrWhiteSpace(asset.RawUrl))
            {
                return new AttachmentResolution(
                    AttachmentStatus.ManualDownloadRequired,
                    null,
                    null,
                    $"No URL captured for '{asset.FileName}'; nothing to download.");
            }

            return new AttachmentResolution(
                AttachmentStatus.ManualDownloadRequired,
                null,
                null,
                $"'{asset.FileName}' requires a logged-in {descriptor.DisplayNameAr} session on {descriptor.Domain} to download. " +
                "No session cookie is configured, so it was NOT fetched automatically. " +
                $"Please open this link in your own logged-in browser and download it manually: {asset.RawUrl}");
        }

        // If we have a cookie, or if it doesn't explicitly require auth, we try to download it.
        // We prefer downloading even for "public" links to ensure they are saved locally 
        // and opened from disk, which avoids the browser "inline" disposition issue.
        var targetUrl = asset.RawUrl ?? asset.Url;
        if (string.IsNullOrWhiteSpace(targetUrl))
        {
            return new AttachmentResolution(
                AttachmentStatus.ManualDownloadRequired,
                null,
                null,
                $"No URL available for '{asset.FileName}'.");
        }

        byte[] data;
        try
        {
            // The injected HttpClient already carries this source's browser headers (its policy
            // User-Agent/Accept/Accept-Language), so no per-request User-Agent override is added -
            // the previous hardcoded Chrome/120 duplicate is gone; every request to a source now
            // uses that source's single policy UA.
            using var request = new HttpRequestMessage(HttpMethod.Get, targetUrl);
            if (!string.IsNullOrWhiteSpace(cookieHeader))
            {
                request.Headers.TryAddWithoutValidation("Cookie", cookieHeader);
            }

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            data = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // If we tried with a cookie and failed, or if it's marked as public but failed,
            // we might want to return ReadyUrl as a fallback if it was supposed to be public.
            if (!asset.RequiresAuth)
            {
                return new AttachmentResolution(AttachmentStatus.ReadyUrl, asset.Url, null, null);
            }

            return new AttachmentResolution(
                AttachmentStatus.AuthFailed,
                null,
                null,
                $"Download attempt for '{asset.FileName}' failed: {ex.Message}. Manual link: {targetUrl}");
        }

        if (LooksLikeHtml(data) && descriptor.IsSameSiteUrl(targetUrl))
        {
            // Getting HTML back from a same-site asset URL is almost certainly a login redirect.
            return new AttachmentResolution(
                AttachmentStatus.AuthFailed,
                null,
                null,
                $"Configured cookie was not accepted for '{asset.FileName}' (received an HTML page instead of a " +
                $"file — likely an expired/invalid session). Manual link: {targetUrl}");
        }

        var destDir = AppPaths.AttachmentsDirectory;
        Directory.CreateDirectory(destDir);
        var localPath = Path.Combine(destDir, asset.FileName);
        await File.WriteAllBytesAsync(localPath, data, cancellationToken);

        return new AttachmentResolution(AttachmentStatus.Downloaded, null, localPath, null);
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
