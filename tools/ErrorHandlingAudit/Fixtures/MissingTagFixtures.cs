// Fixture set for Rule D: raise/catch sites missing [ErrorCode]/[ErrorOutcome] tagging.
namespace ErrorHandlingAudit.Fixtures;

internal static class MissingTagCompliant1
{
    [MostaqlK.Core.ErrorOutcome(MostaqlK.Core.ErrorOutcome.Handled, Label = "MissingTagCompliant1")]
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

internal static class MissingTagCompliant2
{
    [MostaqlK.Core.ErrorCode("FIX-003")]
    [MostaqlK.Core.ErrorOutcome(MostaqlK.Core.ErrorOutcome.Rethrown, Label = "MissingTagCompliant2")]
    internal static void Run()
    {
        try
        {
            DoWork();
        }
        catch (Exception)
        {
            throw;
        }
    }

    private static void DoWork() { }
}

// VIOLATION #1 — catch block with no [ErrorOutcome]/[ErrorCode] tag on the enclosing method.
internal static class MissingTagViolation1
{
    internal static void Run()
    {
        try
        {
            DoWork();
        }
        catch (Exception)
        {
            throw;
        }
    }

    private static void DoWork() { }
}

// VIOLATION #2 — same, different shape (Result.Err arm, no tagging at all).
internal static class MissingTagViolation2
{
    internal static MostaqlK.Core.Result<string> Run(bool ok)
    {
        if (!ok)
        {
            var error = FactoryRuleErrors.SampleFailed("bad state");
            return MostaqlK.Core.Result<string>.Err(error);
        }

        return MostaqlK.Core.Result<string>.Ok("ok");
    }
}
