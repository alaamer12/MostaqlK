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
}
