// Fixture set for Rule A: DomainError/exception construction must go through a module's Errors.cs factory.
namespace ErrorHandlingAudit.Fixtures;

internal static class FactoryRuleErrors
{
    internal static MostaqlK.Core.DomainError SampleFailed(string detail)
        => new(
            Code: "FIX-001",
            InternalMessage: $"Sample failed: {detail}",
            ExternalMessage: "حدث خطأ.",
            FixMessage: "أعد المحاولة.");
}

// COMPLIANT #1 — goes through the module's Errors.cs factory.
internal static class FactoryRuleCompliant1
{
    internal static MostaqlK.Core.DomainError Build(string detail)
        => FactoryRuleErrors.SampleFailed(detail);
}

// COMPLIANT #2 — exception thrown via a factory-returned instance (still routed through Errors.cs).
internal sealed class FactoryRuleCompliantException : Exception
{
    private FactoryRuleCompliantException(string message) : base(message) { }

    internal static FactoryRuleCompliantException Create(string detail)
        => new($"Sample failed: {detail}");
}

internal static class FactoryRuleCompliant2
{
    internal static void Run(string detail)
    {
        throw FactoryRuleCompliantException.Create(detail);
    }
}

// VIOLATION #1 — constructs a DomainError with a bare literal outside of Errors.cs.
internal static class FactoryRuleViolation1
{
    internal static MostaqlK.Core.DomainError Build(string detail)
        => new(
            Code: "FIX-002",
            InternalMessage: $"Sample failed: {detail}",
            ExternalMessage: "حدث خطأ.",
            FixMessage: "أعد المحاولة.");
}

// VIOLATION #2 — `new`s up a custom exception directly instead of going through a factory.
internal static class FactoryRuleViolation2
{
    internal static void Run(string detail)
    {
        throw new InvalidOperationException($"Sample failed: {detail}");
    }
}
