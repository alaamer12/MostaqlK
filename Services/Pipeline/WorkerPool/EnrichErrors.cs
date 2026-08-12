using MostaqlK.Core;

namespace MostaqlK.Services.Pipeline.WorkerPool;

/// <summary>
/// Error factory for <see cref="EnrichmentWorker"/> permanent-failure outcomes. Codes use
/// the "ENRICH" domain (see <see cref="ErrorCodeRegistry"/>).
/// </summary>
public static class EnrichErrors
{
    public static DomainError MaxAttemptsExhausted(long projectId, int attempts, DomainError lastError) => new(
        Code: "ENRICH-001",
        InternalMessage: $"Project {projectId} failed enrichment after {attempts} attempts. Last error: {lastError.InternalMessage}",
        ExternalMessage: "تعذر جلب تفاصيل المشروع بعد عدة محاولات.",
        FixMessage: "سيتم تجاهل هذا المشروع؛ قد يظهر مجدداً في الفحص التالي إن لم يتم حفظه.",
        Cause: lastError.Cause);

    /// <summary>
    /// An exception escaping the whole per-project block in <c>EnrichmentWorker.RunAsync</c>
    /// (not a failing <c>Result</c>). The worker logs this and moves on to the next project
    /// instead of ending its loop.
    /// </summary>
    public static DomainError Unexpected(long projectId, Exception cause) => new(
        Code: "ENRICH-002",
        InternalMessage: $"Unexpected exception while enriching project {projectId}: {cause.Message}",
        ExternalMessage: "حدث خطأ غير متوقع أثناء معالجة أحد المشاريع.",
        FixMessage: "تم تجاوز هذا المشروع والاستمرار في المعالجة؛ راجع سجل الأحداث للتفاصيل.",
        Cause: cause);
}
