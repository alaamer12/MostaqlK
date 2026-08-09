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
}
