using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using Microsoft.Win32.SafeHandles;
using MostaqlK.Core;
using MostaqlK.Core.Diagnostics;
using MostaqlK.Core.Platform;

namespace MostaqlK.Services.Diagnostics;

/// <summary>
/// Dedicated, thread-aware crash diagnostics service for MostaqlK.
/// Synchronously and reliably writes full diagnostic context (thread state, memory, process uptime,
/// complete stack traces across all inner exceptions) to <c>crash.log</c> and generates native minidumps without throwing.
/// </summary>
public static class CrashReporter
{
    private static readonly object WriteLock = new();
    private static int _isRegistered;
    private static int _firstChanceLogged;
    private const int FirstChanceLogCap = 400;
    private static readonly DateTime ProcessStartTimeUtc = DateTime.UtcNow;

    private static string Trim(string? value, int max) =>
        string.IsNullOrEmpty(value) ? string.Empty : (value.Length <= max ? value : value[..max] + "…");

#if WINDOWS || NET10_0_WINDOWS10_0_19041_0_OR_GREATER || WINDOWS10_0_17763_0_OR_GREATER
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int UnhandledExceptionFilterDelegate(IntPtr exceptionPointers);

    private static UnhandledExceptionFilterDelegate? _nativeFilterDelegate;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr SetUnhandledExceptionFilter(UnhandledExceptionFilterDelegate lpTopLevelExceptionFilter);

    [DllImport("dbghelp.dll", SetLastError = true)]
    private static extern bool MiniDumpWriteDump(
        IntPtr hProcess,
        uint processId,
        SafeFileHandle hFile,
        uint dumpType,
        ref MINIDUMP_EXCEPTION_INFORMATION exceptionParam,
        IntPtr userStreamParam,
        IntPtr callbackParam);

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct MINIDUMP_EXCEPTION_INFORMATION
    {
        public uint ThreadId;
        public IntPtr ExceptionPointers;
        [MarshalAs(UnmanagedType.Bool)]
        public bool ClientPointers;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct EXCEPTION_RECORD
    {
        public uint ExceptionCode;
        public uint ExceptionFlags;
        public IntPtr ExceptionRecord;
        public IntPtr ExceptionAddress;
        public uint NumberParameters;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct EXCEPTION_POINTERS
    {
        public IntPtr ExceptionRecord;
        public IntPtr ContextRecord;
    }

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentProcessId();

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();
#endif

    /// <summary>
    /// Registers global unhandled exception hooks across <see cref="AppDomain"/>, <see cref="TaskScheduler"/>,
    /// and native Win32 Structured Exception Handling (SEH).
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
            InteractionLogger.Fault("AppDomain.UnhandledException", ex);
        };

        TaskScheduler.UnobservedTaskException += (sender, args) =>
        {
            Report("TaskScheduler.UnobservedTaskException", args.Exception, isFatal: false);
            InteractionLogger.Fault("TaskScheduler.UnobservedTaskException", args.Exception);
            args.SetObserved();
        };

        // THE funnel: every CAUGHT exception that reaches InteractionLogger.Fault now also reaches
        // telemetry, sampled by HandledReportingPolicy. The observer never sends from the calling
        // thread (a composition callback must not pay SDK work; animation-safety), only enqueues.
        InteractionLogger.FaultObserver = ReportHandled;

        // The sibling funnel for observed-but-unpropagated Result failures: deduped by error CODE,
        // so a permanent 403-per-poll is one budgeted signal, not silence (local) and not a storm
        // (naive forwarding). The dev69 lesson generalized: an error nobody is told about is an
        // error that will keep happening.
        InteractionLogger.FailureObserver = ReportDomainFailure;

        // ALWAYS-on, near-free: count repeats of the same (type, message-prefix) throw and promote
        // a repeat offender through the funnel exactly once per storm. This is the net under the
        // catch audit — a swallowed throw in a file nobody logged from still becomes visible, which
        // is precisely the IterationCount pattern that hid for nine days. Full first-chance LOGGING
        // stays opt-in below; counting is not logging.
        AppDomain.CurrentDomain.FirstChanceException += (sender, args) =>
        {
            try
            {
                var key = FirstChanceStormPolicy.KeyFor(args.Exception);
                if (FirstChanceStormPolicy.NoteThrow(key, DateTime.UtcNow))
                {
                    InteractionLogger.Fault("FirstChance.Storm", args.Exception, new { stormKey = key });
                }
            }
            catch
            {
                // the probe must never become the incident
            }
        };

        InteractionLogger.Mark("Diagnostics.HandlersRegistered", "A",
            new { firstChanceDiag = Environment.GetEnvironmentVariable("MOSTAQLK_DIAG_COM") == "1" });

        // Opt-in first-chance probe (MOSTAQLK_DIAG_COM=1).
        //
        // It used to filter on InvalidCastException/COMException/EntryPointNotFoundException, and
        // that filter is what hid the 2026-10-28 fail-fast: the stowed error in those dumps is
        // 0x80004005, which is what a PLAIN System.Exception marshals to. A type filter on the
        // probe cannot see the bug that kills the process, so the probe now records every throw.
        // Everything still crosses a WinRT boundary in a MAUI app, so the exception's own stack is
        // often empty — the live stack of the throwing thread is what names the call.
        //
        // Gated by an environment variable and hard-capped, because a chatty app throws plenty of
        // handled exceptions; a normal launch never sets it.
        if (Environment.GetEnvironmentVariable("MOSTAQLK_DIAG_COM") == "1")
        {
            AppDomain.CurrentDomain.FirstChanceException += (sender, args) =>
            {
                if (System.Threading.Interlocked.Increment(ref _firstChanceLogged) > FirstChanceLogCap)
                {
                    return;
                }

                InteractionLogger.Fault("FirstChance", args.Exception, new
                {
                    Type = args.Exception.GetType().FullName,
                    args.Exception.HResult,
                    Message = Trim(args.Exception.Message, 300),
                    Thread = Environment.CurrentManagedThreadId,
                    Stack = args.Exception.StackTrace ?? "(no stack)",
                    LiveStack = Trim(new StackTrace(true).ToString(), 4000),
                });
            };
        }

#if WINDOWS || NET10_0_WINDOWS10_0_19041_0_OR_GREATER || WINDOWS10_0_17763_0_OR_GREATER
        RegisterNativeExceptionFilter();
#endif
    }

    private static readonly ConcurrentQueue<Action> HandledSendQueue = new();
    private static int _handledPumpRunning;

    /// <summary>
    /// Forwards a CAUGHT exception to Sentry, budgeted per checkpoint+type and always off the
    /// calling thread. The local <c>FAULT</c> line was already written by <see cref="InteractionLogger"/>
    /// before this runs — this method only decides whether the network is also told. Never throws.
    /// </summary>
    public static void ReportHandled(string source, Exception exception, object? context = null)
    {
        try
        {
            if (exception is null || HandledReportingPolicy.IsExempt(source))
            {
                return;
            }

            var decision = HandledReportingPolicy.Decide(source, exception, DateTime.UtcNow);
            if (decision is HandledReportDecision.Exempt or HandledReportDecision.Suppress)
            {
                return;
            }

            // Same exception object reported twice (Report + Fault, or Fault twice) = one event.
            if (!HandledReportingPolicy.TryMarkExceptionForwarded(exception))
            {
                return;
            }

            var suppressed = decision == HandledReportDecision.ReportWithSuppressed
                ? HandledReportingPolicy.TakeSuppressedCount(source, exception)
                : 0;

            EnqueueHandledSend(() =>
            {
                try
                {
                    Sentry.SentrySdk.CaptureException(exception, handled: true, terminal: false, scope =>
                    {
                        scope.SetTag("source", source);
                        if (suppressed > 0)
                        {
                            scope.SetExtra("suppressed_since_last", suppressed);
                        }

                        if (context is not null)
                        {
                            scope.SetExtra("context", SafeSerialize(context));
                        }
                    });
                }
                catch
                {
                    // Fail-safe: a dropped forward never affects the app or the local log.
                }
            });
        }
        catch
        {
            // Fail-safe: the reporting path must never become a crash.
        }
    }

    private static void EnqueueHandledSend(Action work)
    {
        HandledSendQueue.Enqueue(work);
        if (Interlocked.CompareExchange(ref _handledPumpRunning, 1, 0) == 0)
        {
            _ = Task.Run(DrainHandledSends);
        }
    }

    /// <summary>
    /// Forwards an observed-but-unpropagated <see cref="DomainError"/> (the value form of a
    /// failure — no exception was thrown anywhere). Budgeted by <c>checkpoint|error.Code</c>: a
    /// permanent failure repeating every poll cycle sends its first three, then a suppressed-count
    /// summary, then quiet — the local ERROR line remains the complete record either way.
    /// Never throws.
    /// </summary>
    public static void ReportDomainFailure(string checkpoint, DomainError error, object? context = null)
    {
        try
        {
            if (error is null || HandledReportingPolicy.IsExempt(checkpoint))
            {
                return;
            }

            var decision = HandledReportingPolicy.Decide(checkpoint, error.Code, DateTime.UtcNow);
            if (decision is HandledReportDecision.Exempt or HandledReportDecision.Suppress)
            {
                return;
            }

            var suppressed = decision == HandledReportDecision.ReportWithSuppressed
                ? HandledReportingPolicy.TakeSuppressedCount(checkpoint, error.Code)
                : 0;

            EnqueueHandledSend(() =>
            {
                try
                {
                    void Decorate(Sentry.Scope scope)
                    {
                        scope.Level = Sentry.SentryLevel.Warning;
                        scope.SetTag("source", checkpoint);
                        scope.SetTag("domain_code", error.Code);
                        scope.SetTag("handled", "true");
                        scope.SetExtra("internal_message", Truncate(error.InternalMessage));
                        scope.SetExtra("external_message", Truncate(error.ExternalMessage));
                        scope.SetExtra("fix_message", Truncate(error.FixMessage));
                        if (suppressed > 0)
                        {
                            scope.SetExtra("suppressed_since_last", suppressed);
                        }

                        if (context is not null)
                        {
                            scope.SetExtra("context", SafeSerialize(context));
                        }
                    }

                    if (error.Cause is Exception cause)
                    {
                        Sentry.SentrySdk.CaptureException(cause, handled: true, terminal: false, Decorate);
                    }
                    else
                    {
                        Sentry.SentrySdk.CaptureMessage(
                            $"[{checkpoint}] domain failure {error.Code}: {Truncate(error.InternalMessage)}",
                            Decorate);
                    }
                }
                catch
                {
                    // Fail-safe: a dropped forward never affects the app or the local log.
                }
            });
        }
        catch
        {
            // Fail-safe: the reporting path must never become a crash.
        }
    }

    private static string Truncate(string? value) =>
        string.IsNullOrEmpty(value) ? string.Empty : (value.Length <= 300 ? value : value[..300] + "…");

    private static void DrainHandledSends()
    {
        try
        {
            while (HandledSendQueue.TryDequeue(out var work))
            {
                try
                {
                    work();
                }
                catch
                {
                    // one poisoned item may not stop the drain
                }
            }

            // Flush AFTER the drain, on this background thread: the SDK's queue is memory-only, so
            // without this a process that fail-fasts seconds after a caught fault takes the unsent
            // envelope with it (CacheDirectoryPath persists transport failures, not un-queued
            // sends). Bounded by the reporting ceiling, so the flush can never become a stall.
            try { Sentry.SentrySdk.Flush(TimeSpan.FromSeconds(5)); } catch { }
        }
        finally
        {
            Interlocked.Exchange(ref _handledPumpRunning, 0);
            if (!HandledSendQueue.IsEmpty && Interlocked.CompareExchange(ref _handledPumpRunning, 1, 0) == 0)
            {
                _ = Task.Run(DrainHandledSends);
            }
        }
    }

#if WINDOWS || NET10_0_WINDOWS10_0_19041_0_OR_GREATER || WINDOWS10_0_17763_0_OR_GREATER
    private static IntPtr _previousFilter = IntPtr.Zero;

    private static void RegisterNativeExceptionFilter()
    {
        try
        {
            _nativeFilterDelegate = NativeUnhandledExceptionCallback;
            _previousFilter = SetUnhandledExceptionFilter(_nativeFilterDelegate);
        }
        catch
        {
            // Fail-safe: native handler registration should never crash startup
        }
    }

    private static int NativeUnhandledExceptionCallback(IntPtr exceptionPointers)
    {
        try
        {
            uint exceptionCode = 0;
            IntPtr exceptionAddress = IntPtr.Zero;

            if (exceptionPointers != IntPtr.Zero)
            {
                try
                {
                    var pointers = Marshal.PtrToStructure<EXCEPTION_POINTERS>(exceptionPointers);
                    if (pointers.ExceptionRecord != IntPtr.Zero)
                    {
                        var record = Marshal.PtrToStructure<EXCEPTION_RECORD>(pointers.ExceptionRecord);
                        exceptionCode = record.ExceptionCode;
                        exceptionAddress = record.ExceptionAddress;
                    }
                }
                catch { }
            }

            if (!NativeExceptionPolicy.ShouldWriteMinidump(exceptionCode))
            {
                return 0; // EXCEPTION_CONTINUE_SEARCH
            }

            var sb = new StringBuilder();
            var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff");
            var codeName = NativeExceptionPolicy.NameFor(exceptionCode);

            sb.AppendLine("================================================================================");
            sb.AppendLine($"[{timestamp} UTC] [FATAL/NATIVE_SEH_CRASH] Source: Win32.UnhandledExceptionFilter");
            sb.AppendLine("================================================================================");
            sb.AppendLine("-- NATIVE EXCEPTION CONTEXT --");
            sb.AppendLine($"  Exception Code: {codeName}");
            sb.AppendLine($"  Fault Address: 0x{exceptionAddress.ToInt64():X16}");
            sb.AppendLine($"  Current Thread ID: {GetCurrentThreadId()}");
            sb.AppendLine($"  Process ID: {GetCurrentProcessId()}");

            AppendEnvironmentTelemetry(sb);

            sb.AppendLine("================================================================================");
            sb.AppendLine();

            var logText = sb.ToString();

            lock (WriteLock)
            {
                try
                {
                    var logFilePath = AppPaths.CrashLogFilePath;
                    var logDir = Path.GetDirectoryName(logFilePath);
                    if (!string.IsNullOrEmpty(logDir) && !Directory.Exists(logDir))
                    {
                        Directory.CreateDirectory(logDir);
                    }
                    File.AppendAllText(logFilePath, logText, Encoding.UTF8);
                }
                catch { }
            }

            // Write MiniDump
            try
            {
                var dumpPath = AppPaths.CrashDumpFilePath;
                var dumpDir = Path.GetDirectoryName(dumpPath);
                if (!string.IsNullOrEmpty(dumpDir) && !Directory.Exists(dumpDir))
                {
                    Directory.CreateDirectory(dumpDir);
                }

                using var fs = new FileStream(dumpPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
                var dumpExceptionInfo = new MINIDUMP_EXCEPTION_INFORMATION
                {
                    ThreadId = GetCurrentThreadId(),
                    ExceptionPointers = exceptionPointers,
                    ClientPointers = false
                };

                // MiniDumpWithIndirectlyReferencedMemory (0x40) | MiniDumpWithThreadInfo (0x1000)
                uint dumpFlags = 0x00000000 | 0x00000040 | 0x00001000;
                MiniDumpWriteDump(
                    GetCurrentProcess(),
                    GetCurrentProcessId(),
                    fs.SafeFileHandle,
                    dumpFlags,
                    ref dumpExceptionInfo,
                    IntPtr.Zero,
                    IntPtr.Zero);
            }
            catch { }
        }
        catch { }

        // EXCEPTION_CONTINUE_SEARCH (0) allows Windows Error Reporting / OS default crash handling to proceed
        return 0;
    }
#endif

    /// <summary>
    /// Checks if a previous native unhandled crash minidump exists on disk, logs the post-mortem
    /// incident to <c>crash.log</c>, and forwards telemetry to Sentry.
    /// </summary>
    public static void CheckAndReportPreviousCrashes()
    {
        try
        {
            // Search in today's log directory as well as base log directory / subdirectories
            var dumpPaths = new List<string>();
            var todayDump = AppPaths.CrashDumpFilePath;
            if (File.Exists(todayDump))
            {
                dumpPaths.Add(todayDump);
            }

            var legacyRootDump = Path.Combine(AppPaths.BaseLogsDirectory, "crash.dmp");
            if (File.Exists(legacyRootDump) && !dumpPaths.Contains(legacyRootDump))
            {
                dumpPaths.Add(legacyRootDump);
            }

            if (Directory.Exists(AppPaths.BaseLogsDirectory))
            {
                try
                {
                    foreach (var dmp in Directory.GetFiles(AppPaths.BaseLogsDirectory, "crash.dmp", SearchOption.AllDirectories))
                    {
                        if (!dumpPaths.Contains(dmp))
                        {
                            dumpPaths.Add(dmp);
                        }
                    }
                }
                catch { }
            }

            foreach (var dumpPath in dumpPaths)
            {
                try
                {
                    if (!File.Exists(dumpPath)) continue;

                    var fileInfo = new FileInfo(dumpPath);
                    var dumpSizeKb = Math.Max(1, fileInfo.Length / 1024);
                    var crashTime = fileInfo.LastWriteTimeUtc;

                    Report(
                        "PostMortem.NativeCrashDetected",
                        new ApplicationException($"Previous session terminated abnormally with native unhandled crash (Minidump: {dumpSizeKb} KB, TimeUtc: {crashTime:yyyy-MM-dd HH:mm:ss})"),
                        new { MinidumpSizeKb = dumpSizeKb, TimestampUtc = crashTime, DumpPath = dumpPath },
                        isFatal: false);

                    try
                    {
                        if (Sentry.SentrySdk.IsEnabled)
                        {
                            Sentry.SentrySdk.CaptureMessage(
                                $"Previous session terminated with native SEH crash (Minidump: {dumpSizeKb} KB)",
                                scope =>
                                {
                                    scope.Level = Sentry.SentryLevel.Fatal;
                                    scope.SetTag("crash_type", "native_seh");
                                    scope.SetTag("post_mortem", "true");
                                    scope.SetExtra("dump_size_kb", dumpSizeKb);
                                    scope.SetExtra("crash_time_utc", crashTime.ToString("o"));
                                    if (File.Exists(AppPaths.CrashLogFilePath))
                                    {
                                        scope.AddAttachment(AppPaths.CrashLogFilePath);
                                    }
                                });
                        }
                    }
                    catch { }

                    // Archive the dump file in its parent directory
                    try
                    {
                        var parentDir = Path.GetDirectoryName(dumpPath) ?? AppPaths.LogsDirectory;
                        var archivePath = Path.Combine(parentDir, $"crash-{crashTime:yyyyMMdd-HHmmss}.dmp");
                        if (File.Exists(archivePath))
                        {
                            File.Delete(archivePath);
                        }
                        File.Move(dumpPath, archivePath);
                    }
                    catch
                    {
                        try { File.Delete(dumpPath); } catch { }
                    }
                }
                catch { }
            }

            // Enforce log retention policy (prune daily directories and legacy logs older than 7 days)
            try
            {
                AppPaths.PurgeOldLogs(7);
            }
            catch { }
        }
        catch
        {
            // Fail-safe
        }
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
                    var logFilePath = AppPaths.CrashLogFilePath;
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

            // Forward to Sentry SDK — unless the handled-observer already claimed THIS exception
            // object (sites like TickerGuard call Report and Fault on one throw; two events for
            // one exception is the spam this guard exists to stop). The crash.log write above is
            // unconditional either way.
            try
            {
                if (exception is null || HandledReportingPolicy.TryMarkExceptionForwarded(exception))
                {
                    if (exception != null)
                    {
                        Sentry.SentrySdk.CaptureException(exception, handled: exception is not null && !isFatal, terminal: isFatal, scope =>
                        {
                            scope.SetTag("source", source);
                            scope.SetTag("fatal", isFatal ? "true" : "false");
                            if (context != null)
                            {
                                scope.SetExtra("context", SafeSerialize(context));
                            }
                        });
                    }
                    else
                    {
                        Sentry.SentrySdk.CaptureMessage($"[{severity}] Source: {source}", scope =>
                        {
                            scope.SetTag("source", source);
                            scope.SetTag("fatal", isFatal ? "true" : "false");
                            if (context != null)
                            {
                                scope.SetExtra("context", SafeSerialize(context));
                            }
                        });
                    }
                }
            }
            catch
            {
                // Fail-safe: ignore Sentry forwarding failures
            }
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
