using MostaqlK.Core;

namespace MostaqlK.Services.Pipeline.DiffEngine;

/// <summary>
/// Error factory for diff-engine failures. Codes use the "DIFF" domain
/// (see <see cref="ErrorCodeRegistry"/>).
/// </summary>
public static class DiffErrors
{
    public static DomainError KnownStateUnavailable(Exception cause) => new(
        Code: "DIFF-001",
        InternalMessage: $"Failed to load known-state for diffing: {cause.Message}",
        ExternalMessage: "تعذر مقارنة المشاريع الجديدة بالمشاريع المعروفة.",
        Cause: cause);
}
