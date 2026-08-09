using MostaqlK.Core;

namespace MostaqlK.Infrastructure.Http;

/// <summary>
/// Error factory for raw HTTP transport failures against the Mostaql website.
/// Codes use the "HTTP" domain (see <see cref="ErrorCodeRegistry"/>).
/// </summary>
public static class HttpErrors
{
    public static DomainError RequestFailed(string url, Exception cause) => new(
        Code: "HTTP-001",
        InternalMessage: $"HTTP request to '{url}' failed: {cause.Message}",
        ExternalMessage: "تعذر الاتصال بموقع مستقل.",
        FixMessage: "تحقق من اتصال الإنترنت وحاول مرة أخرى.",
        Cause: cause);

    public static DomainError UnexpectedStatusCode(string url, int statusCode) => new(
        Code: "HTTP-002",
        InternalMessage: $"Request to '{url}' returned unexpected status code {statusCode}.",
        ExternalMessage: "استجابة غير متوقعة من موقع مستقل.");

    public static DomainError Timeout(string url, Exception cause) => new(
        Code: "HTTP-003",
        InternalMessage: $"Request to '{url}' timed out: {cause.Message}",
        ExternalMessage: "استغرق الاتصال بموقع مستقل وقتاً طويلاً.",
        FixMessage: "تحقق من اتصال الإنترنت وحاول مرة أخرى.",
        Cause: cause);

    public static DomainError NotFound(string url) => new(
        Code: "HTTP-004",
        InternalMessage: $"Request to '{url}' returned 404 Not Found.",
        ExternalMessage: "المشروع غير موجود.");

    public static DomainError Unexpected(string url, Exception cause) => new(
        Code: "HTTP-005",
        InternalMessage: $"Unexpected error requesting '{url}': {cause.Message}",
        ExternalMessage: "حدث خطأ غير متوقع أثناء الاتصال بموقع مستقل.",
        Cause: cause);

    public static DomainError ParseFailed(string url, Exception cause) => new(
        Code: "HTTP-006",
        InternalMessage: $"Failed to parse response from '{url}': {cause.Message}",
        ExternalMessage: "تعذر تحليل بيانات موقع مستقل.",
        Cause: cause);
}
