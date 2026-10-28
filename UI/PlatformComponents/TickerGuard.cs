using System;
using System.Collections.Generic;
using Microsoft.Maui.Dispatching;
using MostaqlK.Services.Diagnostics;

namespace MostaqlK.UI.PlatformComponents;

/// <summary>
/// The one place an <see cref="IDispatcherTimer"/> tick body is allowed to run.
/// <para>
/// WHY THIS EXISTS (proven from crash dumps, 2026-10-28): a tick handler that lets an exception
/// escape does NOT reach <c>Microsoft.UI.Xaml.Application.UnhandledException</c>. WinUI raises
/// <c>DispatcherQueueTimer</c> ticks through its own delegate adapter, and an exception leaving that
/// adapter is stowed by combase and turned into <c>RoFailFastWithErrorContextInternal2</c> →
/// <c>0xC000027B</c> — an immediate process kill with no managed handler in the path. Two dumps
/// captured the same chain (<c>DispatcherQueueTimer::TimerCallback</c> →
/// <c>TimeoutManager::Callback_OnDispatch</c> → <c>Microsoft.UI.Xaml.dll</c> → fail-fast) carrying
/// different inner errors (0x80004002, 0x80004005), so the fatal thing is the escape itself, not one
/// specific throw. The app runs eight of these tickers; every one of them is now routed here.
/// </para>
/// <para>
/// Policy: swallow (the process is worth more than one animation frame), log the FIRST failure of a
/// given ticker with its full stack so the real bug stays findable, and stop the timer after
/// <see cref="ConsecutiveFailuresBeforeStop"/> failures in a row — a ticker that throws every frame
/// at ~30fps must not become an exception storm in an app that runs for days in the tray.
/// </para>
/// </summary>
public static class TickerGuard
{
    private const int ConsecutiveFailuresBeforeStop = 30;

    private static readonly object Gate = new();
    private static readonly Dictionary<string, int> FailureStreak = [];
    private static readonly HashSet<string> Reported = [];

    /// <summary>
    /// True once a ticker has failed <see cref="ConsecutiveFailuresBeforeStop"/> times in a row.
    /// A self-repeating callback (a MAUI <c>Animation</c> commit has no timer handle to stop) uses
    /// this in its own repeat condition so a permanently broken frame loop parks itself instead of
    /// throwing sixty times a second for the life of the process.
    /// </summary>
    public static bool ShouldStop(string ticker)
    {
        lock (Gate)
        {
            return FailureStreak.TryGetValue(ticker, out var streak) && streak >= ConsecutiveFailuresBeforeStop;
        }
    }

    /// <summary>
    /// Invoke <paramref name="body"/> for <paramref name="ticker"/>, absorbing anything it throws.
    /// </summary>
    /// <param name="timer">The timer to park if this ticker proves permanently broken; null for callbacks that own no timer.</param>
    /// <param name="ticker">Stable log identity — the owning unit's name.</param>
    /// <param name="body">The tick work.</param>
    public static void Run(IDispatcherTimer? timer, string ticker, Action body)
    {
        try
        {
            body();

            lock (Gate)
            {
                FailureStreak.Remove(ticker);
            }
        }
        catch (Exception ex)
        {
            int streak;
            bool firstForThisTicker;

            lock (Gate)
            {
                streak = FailureStreak.TryGetValue(ticker, out var previous) ? previous + 1 : 1;
                FailureStreak[ticker] = streak;
                firstForThisTicker = Reported.Add(ticker);
            }

            if (firstForThisTicker)
            {
                // The stack here is the point of the whole exercise: it names the call that would
                // otherwise only ever be visible as a fail-fast offset in a minidump. Reporting must
                // never become a second way to lose the process, so it is wrapped too.
                try
                {
                    CrashReporter.Report($"TickerGuard.{ticker}", ex, new { ticker, streak }, isFatal: false);
                    InteractionLogger.Fault($"TickerGuard.{ticker}", ex, new { ticker, streak });
                }
                catch
                {
                    // A diagnostics sink that is itself broken is not worth a crash.
                }
            }

            if (streak >= ConsecutiveFailuresBeforeStop)
            {
                timer?.Stop();
            }
        }
    }
}
