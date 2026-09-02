using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using MostaqlK.Core.Platform;

namespace MostaqlK.Services.Diagnostics;

/// <summary>
/// Dedicated, thread-aware crash diagnostics service for MostaqlK.
/// Synchronously and reliably writes full diagnostic context (thread state, memory, process uptime,
/// complete stack traces across all inner exceptions) to <c>crash.log</c> without throwing.
/// </summary>
public static class CrashReporter
{
    private static readonly object WriteLock = new();
    private static readonly Lazy<string> CrashLogPath = new(() => AppPaths.CrashLogFilePath);
    private static int _isRegistered;
    private static readonly DateTime ProcessStartTimeUtc = DateTime.UtcNow;

    /// <summary>
    /// Registers global unhandled exception hooks across <see cref="AppDomain"/> and <see cref="TaskScheduler"/>.
    /// Idempotent: safe to call multiple times.
    /// </summary>
    public static void RegisterGlobalHandlers()
    {
        if (Interlocked.Exchange(ref _isRegistered, 1) == 1)
        {
            return;
        }

        AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
        {
            var ex = args.ExceptionObject as Exception ??
                     new Exception($"Non-Exception Unhandled Object: {args.ExceptionObject}");
            Report("AppDomain.UnhandledException", ex, isFatal: args.IsTerminating);
        };

        TaskScheduler.UnobservedTaskException += (sender, args) =>
        {
            Report("TaskScheduler.UnobservedTaskException", args.Exception, isFatal: false);
            args.SetObserved();
        };
    }

    /// <summary>
    /// Records an exception or crash event with full thread and environment context to <c>crash.log</c>.
    /// This method is fail-safe and never throws.
    /// </summary>
    /// <param name="source">Component or event source identifying where the fault originated.</param>
    /// <param name="exception">The exception instance or null.</param>
    /// <param name="context">Optional extra diagnostic context object.</param>
    /// <param name="isFatal">True if this exception is unhandled and terminating the process.</param>
    public static void Report(string source, Exception? exception, object? context = null, bool isFatal = false)
    {
        try
        {
            var sb = new StringBuilder();
            var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff");
            var severity = isFatal ? "FATAL/CRASH" : "ERROR/FAULT";

            sb.AppendLine("================================================================================");
            sb.AppendLine($"[{timestamp} UTC] [{severity}] Source: {source}");
            sb.AppendLine("================================================================================");

            // 1. Thread Diagnostics
            AppendThreadContext(sb);

            // 2. Process & Environment Telemetry
            AppendEnvironmentTelemetry(sb);

            // 3. Exception Details
            if (exception != null)
            {
                sb.AppendLine("-- EXCEPTION DETAILS --");
                AppendExceptionTree(sb, exception, 0);
            }
            else
            {
                sb.AppendLine("-- EXCEPTION DETAILS --");
                sb.AppendLine("  No exception object provided.");
            }

            // 4. Context Data
            if (context != null)
            {
                sb.AppendLine("-- CONTEXT DATA --");
                sb.AppendLine($"  {SafeSerialize(context)}");
            }

            sb.AppendLine("================================================================================");
            sb.AppendLine();

            var logText = sb.ToString();

            lock (WriteLock)
            {
                try
                {
                    var logFilePath = CrashLogPath.Value;
                    var logDir = Path.GetDirectoryName(logFilePath);
                    if (!string.IsNullOrEmpty(logDir) && !Directory.Exists(logDir))
                    {
                        Directory.CreateDirectory(logDir);
                    }
                    File.AppendAllText(logFilePath, logText, Encoding.UTF8);
                }
                catch
                {
                    // Fail-safe: try fallback path in Temp if main path fails
                    try
                    {
                        var fallbackPath = Path.Combine(Path.GetTempPath(), "mostaqlk-crash.log");
                        File.AppendAllText(fallbackPath, logText, Encoding.UTF8);
                    }
                    catch
                    {
                        // Final silent catch - never take down process from crash logger
                    }
                }
            }

            // Also mirror to debug output in development
            Debug.WriteLine($"[CrashReporter] [{severity}] [{source}]: {exception?.Message}");
        }
        catch
        {
            // Fail-safe: crash logger must never throw
        }
    }

    private static void AppendThreadContext(StringBuilder sb)
    {
        try
        {
            var thread = Thread.CurrentThread;
            sb.AppendLine("-- THREAD CONTEXT --");
            sb.AppendLine($"  Managed Thread ID: {thread.ManagedThreadId}");
            sb.AppendLine($"  Thread Name: {(!string.IsNullOrWhiteSpace(thread.Name) ? thread.Name : "<unnamed>")}");
            sb.AppendLine($"  Is ThreadPool: {thread.IsThreadPoolThread}");
            sb.AppendLine($"  Is Background: {thread.IsBackground}");
            sb.AppendLine($"  Thread State: {thread.ThreadState}");
            sb.AppendLine($"  Thread Priority: {thread.Priority}");
            sb.AppendLine($"  Has SynchronizationContext: {SynchronizationContext.Current != null} ({SynchronizationContext.Current?.GetType().Name ?? "None"})");
        }
        catch (Exception ex)
        {
            sb.AppendLine($"  Failed to retrieve thread context: {ex.Message}");
        }
    }

    private static void AppendEnvironmentTelemetry(StringBuilder sb)
    {
        try
        {
            var uptime = DateTime.UtcNow - ProcessStartTimeUtc;
            var workingSetMb = Environment.WorkingSet / (1024.0 * 1024.0);
            var gcMemoryMb = GC.GetTotalMemory(false) / (1024.0 * 1024.0);

            sb.AppendLine("-- ENVIRONMENT & PROCESS TELEMETRY --");
            sb.AppendLine($"  Process ID: {Environment.ProcessId}");
            sb.AppendLine($"  Process Uptime: {uptime:dd\\.hh\\:mm\\:ss\\.fff}");
            sb.AppendLine($"  Working Set (RAM): {workingSetMb:F2} MB");
            sb.AppendLine($"  GC Total Memory: {gcMemoryMb:F2} MB");
            sb.AppendLine($"  OS Version: {Environment.OSVersion} ({RuntimeInformation.OSDescription})");
            sb.AppendLine($"  .NET Framework: {RuntimeInformation.FrameworkDescription}");
            sb.AppendLine($"  Process Architecture: {RuntimeInformation.ProcessArchitecture}");
            sb.AppendLine($"  Processor Count: {Environment.ProcessorCount}");
        }
        catch (Exception ex)
        {
            sb.AppendLine($"  Failed to retrieve environment telemetry: {ex.Message}");
        }
    }

    private static void AppendExceptionTree(StringBuilder sb, Exception ex, int depth)
    {
        var indent = new string(' ', (depth + 1) * 2);
        var prefix = depth > 0 ? $"[InnerException Level {depth}] " : "";

        sb.AppendLine($"{indent}{prefix}Type: {ex.GetType().FullName}");
        sb.AppendLine($"{indent}Message: {ex.Message}");
        if (!string.IsNullOrEmpty(ex.Source))
        {
            sb.AppendLine($"{indent}Source: {ex.Source}");
        }
        if (ex.TargetSite != null)
        {
            sb.AppendLine($"{indent}TargetSite: {ex.TargetSite}");
        }
        if (ex.HResult != 0)
        {
            sb.AppendLine($"{indent}HResult: 0x{ex.HResult:X8}");
        }

        if (ex.Data.Count > 0)
        {
            sb.AppendLine($"{indent}Data Dictionary:");
            foreach (DictionaryEntry de in ex.Data)
            {
                sb.AppendLine($"{indent}  {de.Key} = {de.Value}");
            }
        }

        if (!string.IsNullOrWhiteSpace(ex.StackTrace))
        {
            sb.AppendLine($"{indent}StackTrace:");
            using var reader = new StringReader(ex.StackTrace);
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                sb.AppendLine($"{indent}  {line.Trim()}");
            }
        }
        else
        {
            sb.AppendLine($"{indent}StackTrace: <empty>");
        }

        if (ex is AggregateException agg)
        {
            var flattened = agg.Flatten();
            sb.AppendLine($"{indent}Aggregate Inner Exceptions ({flattened.InnerExceptions.Count}):");
            for (int i = 0; i < flattened.InnerExceptions.Count; i++)
            {
                sb.AppendLine($"{indent}--- Item #{i + 1} of {flattened.InnerExceptions.Count} ---");
                AppendExceptionTree(sb, flattened.InnerExceptions[i], depth + 1);
            }
        }
        else if (ex.InnerException != null)
        {
            sb.AppendLine($"{indent}--- Inner Exception ---");
            AppendExceptionTree(sb, ex.InnerException, depth + 1);
        }
    }

    private static string SafeSerialize(object data)
    {
        try
        {
            return JsonSerializer.Serialize(data, new JsonSerializerOptions
            {
                WriteIndented = false
            });
        }
        catch
        {
            return data.ToString() ?? "<unserializable>";
        }
    }
}
