using MostaqlK.Core;

namespace MostaqlK.Services.Pipeline;

/// <summary>
/// Error factory for <see cref="IPollService"/> failures. Codes use the "POLL" domain
/// (see <see cref="ErrorCodeRegistry"/>).
/// </summary>
public static class PollErrors
{
    public static DomainError ListingFetchFailed(Exception cause) => new(
        Code: "POLL-001",
        InternalMessage: $"Failed to fetch listing page: {cause.Message}",
        ExternalMessage: "تعذر تحديث قائمة المشاريع.",
        FixMessage: "سيتم إعادة المحاولة عند دورة الفحص التالية.",
        Cause: cause);

    public static DomainError PollCancelled() => new(
        Code: "POLL-002",
        InternalMessage: "Poll cycle was cancelled.",
        ExternalMessage: "تم إيقاف الفحص.");
}
