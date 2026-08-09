namespace MostaqlK.Core;

/// <summary>
/// Discriminated union representing either a successful value of type <typeparamref name="T"/>
/// or a <see cref="DomainError"/> describing why the operation failed.
/// </summary>
public readonly struct Result<T>
{
    public bool IsOk { get; }
    public bool IsError => !IsOk;

    private readonly T? _value;
    private readonly DomainError? _error;

    private Result(T value)
    {
        IsOk = true;
        _value = value;
        _error = null;
    }

    private Result(DomainError error)
    {
        IsOk = false;
        _value = default;
        _error = error;
    }

    public T Value => IsOk
        ? _value!
        : throw new InvalidOperationException("Cannot access Value on an error Result.");

    public DomainError Error => !IsOk
        ? _error!
        : throw new InvalidOperationException("Cannot access Error on an ok Result.");

    public static Result<T> Ok(T value) => new(value);

    public static Result<T> Err(DomainError error) => new(error);

    public TResult Match<TResult>(Func<T, TResult> onOk, Func<DomainError, TResult> onError) =>
        IsOk ? onOk(_value!) : onError(_error!);

    public void Switch(Action<T> onOk, Action<DomainError> onError)
    {
        if (IsOk)
        {
            onOk(_value!);
        }
        else
        {
            onError(_error!);
        }
    }
}
