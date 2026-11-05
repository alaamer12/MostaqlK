using MostaqlK.Core;

namespace MostaqlK.Infrastructure.Http;

/// <summary>
/// Error factory for raw HTTP transport failures against a monitored website.
/// The user-facing site name is a parameter — shared plumbing must not name a site.
/// Codes use the "HTTP" domain (see <see cref="ErrorCodeRegistry"/>).
/// </summary>
public static class HttpErrors
{
    public static DomainError RequestFailed(string url, Exception cause, string siteName) => new(
        Code: "HTTP-001",
        InternalMessage: $"HTTP request to '{url}' failed: {cause.Message}",
        ExternalMessage: $"تعذر الاتصال بموقع {siteName}.",
        FixMessage: "تحقق من اتصال الإنترنت وحاول مرة أخرى.",
        Cause: cause);

    public static DomainError UnexpectedStatusCode(string url, int statusCode, string siteName) => new(
        Code: "HTTP-002",
        InternalMessage: $"Request to '{url}' returned unexpected status code {statusCode}.",
        ExternalMessage: $"استجابة غير متوقعة من موقع {siteName}.");

    public static DomainError Timeout(string url, Exception cause, string siteName) => new(
        Code: "HTTP-003",
        InternalMessage: $"Request to '{url}' timed out: {cause.Message}",
        ExternalMessage: $"استغرق الاتصال بموقع {siteName} وقتاً طويلاً.",
        FixMessage: "تحقق من اتصال الإنترنت وحاول مرة أخرى.",
        Cause: cause);

    public static DomainError NotFound(string url) => new(
        Code: "HTTP-004",
        InternalMessage: $"Request to '{url}' returned 404 Not Found.",
        ExternalMessage: "المشروع غير موجود.");

    public static DomainError Unexpected(string url, Exception cause, string siteName) => new(
        Code: "HTTP-005",
        InternalMessage: $"Unexpected error requesting '{url}': {cause.Message}",
        ExternalMessage: $"حدث خطأ غير متوقع أثناء الاتصال بموقع {siteName}.",
        Cause: cause);

    public static DomainError ParseFailed(string url, Exception cause, string siteName) => new(
        Code: "HTTP-006",
        InternalMessage: $"Failed to parse response from '{url}': {cause.Message}",
        ExternalMessage: $"تعذر تحليل بيانات موقع {siteName}.",
        Cause: cause);

    // v4 R1.10: an AWS WAF JavaScript challenge answers HTTP 202 (a 2xx, so it is NOT caught by
    // EnsureSuccessStatusCode) with a ~2 KB interstitial that is not the page. Returning it as a
    // successful body is what makes a blocked request look like an empty scrape. This is a distinct
    // error so the caller records it honestly (snapshot error_kind = waf_challenge) and, once the
    // token provider lands, can force-refresh and retry once on exactly this signal.
    public static DomainError WafChallenge(string url, int statusCode, string siteName) => new(
        Code: "HTTP-007",
        InternalMessage: $"Request to '{url}' returned WAF challenge (status {statusCode}); no page content.",
        ExternalMessage: $"انتظر الموقع للتحقق من أنك لست روبوتًا قبل إظهار محتوى {siteName}.");
}
