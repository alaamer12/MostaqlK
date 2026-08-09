namespace MostaqlK.Core.Domain;

/// <summary>
/// Describes the failure of a single item within a batch operation, keeping the item's
/// identity alongside the <see cref="DomainError"/> that caused it to fail.
/// </summary>
public sealed record ItemFailure(string ItemId, DomainError Error);
