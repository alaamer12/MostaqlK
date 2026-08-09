namespace MostaqlK.Core.Domain;

/// <summary>
/// Aggregated outcome of a batch operation (e.g. enriching many discovered project IDs),
/// tracking both successes and per-item failures without aborting the whole batch.
/// </summary>
public sealed class BatchResult<T>
{
    public List<T> Succeeded { get; } = [];

    public List<ItemFailure> Failures { get; } = [];

    public int TotalCount => Succeeded.Count + Failures.Count;

    public void AddSuccess(T item) => Succeeded.Add(item);

    public void AddFailure(ItemFailure failure) => Failures.Add(failure);
}
