// Fixture set for Rule B: caught-and-swallowed exceptions where no ExternalMessage/FixMessage
// propagates anywhere reachable (empty/log-only catch blocks).
namespace ErrorHandlingAudit.Fixtures;

internal static class SwallowedCatchCompliant1
{
    [MostaqlK.Core.ErrorOutcome(MostaqlK.Core.ErrorOutcome.Handled, Label = "SwallowedCatchCompliant1")]
    internal static string Run()
    {
        try
        {
            return "ok";
        }
        catch (Exception ex)
        {
            var error = FactoryRuleErrors.SampleFailed(ex.Message);
            return error.ExternalMessage;
        }
    }
}

internal static class SwallowedCatchCompliant2
{
    [MostaqlK.Core.ErrorOutcome(MostaqlK.Core.ErrorOutcome.Handled, Label = "SwallowedCatchCompliant2")]
    internal static void Run()
    {
        try
        {
            DoWork();
        }
        catch (Exception ex)
        {
            var error = FactoryRuleErrors.SampleFailed(ex.Message);
            MostaqlK.Services.Diagnostics.InteractionLogger.Fault("SwallowedCatchCompliant2", error.InternalMessage);
        }
    }

    private static void DoWork() { }
}

// VIOLATION #1 — empty catch block, exception fully swallowed.
internal static class SwallowedCatchViolation1
{
    internal static void Run()
    {
        try
        {
            DoWork();
        }
        catch (Exception)
        {
        }
    }

    private static void DoWork() { }
}

// VIOLATION #2 — log-only catch block, no ExternalMessage/FixMessage ever produced or propagated.
internal static class SwallowedCatchViolation2
{
    internal static void Run()
    {
        try
        {
            DoWork();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex.Message);
        }
    }

    private static void DoWork() { }
}
