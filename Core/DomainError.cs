namespace MostaqlK.Core;

/// <summary>
/// Structured error carried inside a failing <see cref="Result{T}"/>.
/// Every module defines its own error factory (see Errors.cs per module) that produces
/// instances of this type with module-specific <see cref="Code"/> values.
/// </summary>
public sealed record DomainError(
    string Code,
    string InternalMessage,
    string ExternalMessage,
    string? FixMessage = null,
    Exception? Cause = null)
{
    public override string ToString() => $"[{Code}] {InternalMessage}";
}
