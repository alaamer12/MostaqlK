using System.Text;
using System.Text.Json;

namespace MostaqlK.Services.Diagnostics;

/// <summary>
/// Structured logging sink for UI/backend interaction diagnostics. Every command entry/exit,
/// exception, and ad-hoc A/B checkpoint funnels through here so both Appium tests and a human
/// can inspect a single rolling log file instead of guessing whether a click/command actually
/// reached the backend. Writes are best-effort and never throw — a logging failure must not take
/// down the app or a test run.
/// </summary>
public static class InteractionLogger
{
    private static readonly object Gate = new();
    private static readonly Lazy<string> LogFilePath = new(ResolveLogFilePath);

    /// <summary>
    /// Bracket-style A/B marker. Call with the same <paramref name="checkpoint"/> and a different
    /// <paramref name="variant"/> (conventionally "A" for enter/branch-taken, "B" for exit/other
    /// branch) around a suspect code path, then diff the resulting log lines to prove whether a
    /// given branch actually executed — this is the "set(Label).ToState(...)" diagnostic the
    /// project uses instead of guessing from UI behavior alone.
    /// </summary>
    // FIX (notifications silently never firing, with zero evidence why): every one of these
    // sinks used to be [Conditional("DEBUG")], which the C# compiler strips at every CALL SITE
    // in a Release build - i.e. in the actual shipped/installed exe the user runs day to day,
    // `WindowsToastSender.SendAsync`'s catch block calling `InteractionLogger.Fault(...)` on a
    // failed toast delivery compiled away to nothing. Any real failure (COM registration error,
    // notifications disabled for the app, etc.) was therefore guaranteed to be invisible outside
    // a DEBUG build, which is exactly why this bug went unnoticed "since day one". Logging itself
    // stays best-effort/never-throws (see the try/catch in Write), so keeping it live in Release
    // carries no crash risk - only the (negligible) cost of appending a line to a local file.
    public static void Mark(string checkpoint, string variant, object? data = null)
        => Write("MARK", checkpoint, variant, data, exception: null);

    /// <summary>Logs the start of a traced command/handler invocation. See <see cref="TraceInteractionAttribute"/>.</summary>
    public static void Enter(string interactionName, object? parameters = null)
        => Write("ENTER", interactionName, variant: null, parameters, exception: null);

    /// <summary>Logs the successful completion of a traced command/handler invocation.</summary>
    public static void Exit(string interactionName, object? result = null)
        => Write("EXIT", interactionName, variant: null, result, exception: null);

    /// <summary>Logs an exception thrown/caught during a traced command/handler invocation.</summary>
    public static void Fault(string interactionName, Exception exception, object? data = null)
        => Write("FAULT", interactionName, variant: null, data, exception);

    /// <summary>
    /// Logs a failing <see cref="MostaqlK.Core.Result{T}"/>. This sink did not exist before, which
    /// is exactly how a hard, permanent failure (the listing endpoint answering 403 on every single
    /// poll cycle) could stay completely invisible: <see cref="MostaqlK.Core.DomainError"/> values
    /// were constructed faithfully and then dropped on the floor, and only *exceptions* had
    /// anywhere to go. Any code that observes <c>Result.IsError</c> and does not propagate it to a
    /// caller must report it here.
    /// </summary>
    public static void Failure(string checkpoint, MostaqlK.Core.DomainError error, object? data = null)
        => Write(
            "ERROR",
            checkpoint,
            variant: error.Code,
            data: new
            {
                error.Code,
                error.InternalMessage,
                error.ExternalMessage,
                error.FixMessage,
                Detail = data,
            },
            exception: error.Cause);

    /// <summary>Absolute path of the rolling diagnostics log file, for tests to read/tail.</summary>
    public static string LogFilePath_ForTests => LogFilePath.Value;

    private static void Write(string kind, string checkpoint, string? variant, object? data, Exception? exception)
    {
        try
        {
            var entry = new StringBuilder()
                .Append(DateTimeOffset.Now.ToString("O"))
                .Append(" | ").Append(kind)
                .Append(" | ").Append(checkpoint);

            if (variant is not null)
            {
                entry.Append(" | variant=").Append(variant);
            }

            if (data is not null)
            {
                entry.Append(" | data=").Append(SafeSerialize(data));
            }

            if (exception is not null)
            {
                entry.Append(" | exception=").Append(exception.GetType().Name)
                     .Append(": ").Append(exception.Message);
            }

            entry.Append(Environment.NewLine);

            lock (Gate)
            {
                File.AppendAllText(LogFilePath.Value, entry.ToString(), Encoding.UTF8);
            }
        }
        catch
        {
            // Diagnostics must never crash the app/tests they are trying to help debug.
        }
    }

    private static string SafeSerialize(object data)
    {
        try
        {
            return JsonSerializer.Serialize(data);
        }
        catch
        {
            return data.ToString() ?? "<unserializable>";
        }
    }

    private static string ResolveLogFilePath()
    {
        // FIX ("still no notification, did you add logs to traceback?"): this used to resolve
        // via Microsoft.Maui.Storage.FileSystem.AppDataDirectory, which on an UNPACKAGED
        // (WindowsPackageType=None) Windows build resolves through WinRT's ApplicationData API —
        // that API requires package identity, and while it doesn't always throw outright on this
        // app's Windows App SDK unpackaged setup, it can silently resolve to a different/virtualized
        // folder per run (or, in a non-MAUI-hosted context, an exception here used to fall back to
        // %TEMP%\MostaqlK). Either way the effective path was NOT reliably discoverable: manual
        // checks under %LOCALAPPDATA%, %APPDATA%, %LOCALAPPDATA%\Packages and %TEMP%\MostaqlK all
        // came back empty even while the app was confirmed running, meaning nobody — including this
        // agent — could actually find and read the log to diagnose anything. Now pinned to a fixed,
        // well-known, always-writable location regardless of packaging/identity: %LocalAppData%\MostaqlK.
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MostaqlK");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, "interaction-log.txt");
    }
}
