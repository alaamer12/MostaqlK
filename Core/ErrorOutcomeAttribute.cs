namespace MostaqlK.Core;

/// <summary>
/// Describes what a call site does with a captured <see cref="DomainError"/>/exception:
/// whether the failure was fully handled (surfaced to the user or otherwise acted on),
/// deliberately ignored (best-effort, non-critical path), or rethrown/propagated further up
/// the call stack. Companion to <see cref="ErrorCodeAttribute"/>/<see cref="ErrorCategoryAttribute"/>
/// in <c>ErrorAttributes.cs</c> — apply this at the consuming site (catch block / <c>Result&lt;T&gt;.Err</c>
/// arm), not at the raising site.
/// </summary>
public enum ErrorOutcome
{
    /// <summary>The error was surfaced (UI binding, log, retry, etc.) — nothing was silently dropped.</summary>
    Handled,

    /// <summary>The error was deliberately swallowed on a best-effort path (e.g. <see cref="MostaqlK.Services.Diagnostics"/>-style logging).</summary>
    Ignored,

    /// <summary>The error was rethrown or wrapped and propagated to the caller.</summary>
    Rethrown,
}

/// <summary>
/// Marks a catch block or <c>Result&lt;T&gt;.Err</c> arm with its <see cref="ErrorOutcome"/>.
/// Purely documentation/tooling metadata for the error-handling audit — does not affect runtime
/// behavior. Apply to the enclosing method/local function when the catch block/arm itself cannot
/// carry an attribute.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Constructor | AttributeTargets.Property, AllowMultiple = true)]
public sealed class ErrorOutcomeAttribute : Attribute
{
    public ErrorOutcome Outcome { get; }

    public string? Label { get; set; }

    public ErrorOutcomeAttribute(ErrorOutcome outcome)
    {
        Outcome = outcome;
    }
}
