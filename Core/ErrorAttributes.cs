namespace MostaqlK.Core;

/// <summary>
/// Marks a constant/static field as the canonical error code for a given failure.
/// Purely documentation/tooling metadata — does not affect runtime behavior.
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Method)]
public sealed class ErrorCodeAttribute : Attribute
{
    public string Code { get; }

    public ErrorCodeAttribute(string code)
    {
        Code = code;
    }
}

/// <summary>
/// Categorizes an error by its broad failure class (e.g. Transient, Permanent, Validation).
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Method)]
public sealed class ErrorCategoryAttribute : Attribute
{
    public string Category { get; }

    public ErrorCategoryAttribute(string category)
    {
        Category = category;
    }
}

/// <summary>
/// Marks a method/type as following the "neither" contract: it must never throw for
/// expected failure paths and must always surface failures via <see cref="Result{T}"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class NeitherContractAttribute : Attribute
{
}

/// <summary>
/// Declares which module domain (see <see cref="ErrorCodeRegistry"/>) an error belongs to.
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Method)]
public sealed class ErrorModuleAttribute : Attribute
{
    public string Module { get; }

    public ErrorModuleAttribute(string module)
    {
        Module = module;
    }
}
