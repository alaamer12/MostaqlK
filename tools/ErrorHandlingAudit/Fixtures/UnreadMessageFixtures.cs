// Fixture set for Rule C: Result<T>.Err arms whose ExternalMessage/FixMessage is never read by
// any UI binding (LabelWithSubText, ValidationMessage) or InteractionLogger call.
namespace ErrorHandlingAudit.Fixtures;

internal static class UnreadMessageCompliant1
{
    internal static string Present(MostaqlK.Core.DomainError error)
    {
        // Simulates binding into LabelWithSubText.
        return $"LabelWithSubText: {error.ExternalMessage} / {error.FixMessage}";
    }
}

internal static class UnreadMessageCompliant2
{
    internal static void Present(MostaqlK.Core.DomainError error)
    {
        MostaqlK.Services.Diagnostics.InteractionLogger.Fault("UnreadMessageCompliant2", error.ExternalMessage);
    }
}

// VIOLATION #1 — error captured but ExternalMessage/FixMessage never read anywhere.
internal static class UnreadMessageViolation1
{
    internal static void Present(MostaqlK.Core.DomainError error)
    {
        System.Diagnostics.Debug.WriteLine("An error occurred.");
    }
}

// VIOLATION #2 — only InternalMessage is used; ExternalMessage/FixMessage dropped.
internal static class UnreadMessageViolation2
{
    internal static string Present(MostaqlK.Core.DomainError error)
    {
        return error.InternalMessage;
    }
}
