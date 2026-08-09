using MostaqlK.Core;

namespace MostaqlK.Services.Pipeline;

/// <summary>
/// Error factory for pipeline-level HTTP orchestration failures (rate limiting, retries).
/// Distinct from <see cref="MostaqlK.Infrastructure.Http.HttpErrors"/>, which covers the
/// raw transport-level failures.
/// </summary>
public static class HttpErrors
{
    public static DomainError RateLimitExhausted() => new(
        Code: "HTTP-101",
        InternalMessage: "Rate limiter budget exhausted and wait was cancelled.",
        ExternalMessage: "تم تجاوز الحد المسموح من الطلبات.");
}
