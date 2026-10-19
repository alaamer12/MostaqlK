namespace MostaqlK.Core.Diagnostics;

/// <summary>
/// Platform-neutral "the user really asked to quit" signal. Shared code (tray menu Quit, the
/// close-to-tray Exit choice) calls <see cref="MarkIntentional"/>; the Windows entry point plugs in
/// <see cref="Handler"/> to set the per-process clean-exit event the out-of-process supervisor
/// (<c>Platforms/Windows/AppWatchdog.cs</c>) waits on, so an intentional exit is never mistaken for
/// a crash and relaunched. A no-op on platforms that do not install a handler.
/// </summary>
public static class ExitIntent
{
    private static int _marked;

    /// <summary>Installed by the platform entry point. Must never throw.</summary>
    public static Action? Handler { get; set; }

    /// <summary>True once <see cref="MarkIntentional"/> ran in this process.</summary>
    public static bool IsMarked => Volatile.Read(ref _marked) == 1;

    /// <summary>Records that the coming process exit is intentional. Idempotent and fail-safe.</summary>
    public static void MarkIntentional()
    {
        if (Interlocked.Exchange(ref _marked, 1) == 1)
        {
            return;
        }

        try
        {
            Handler?.Invoke();
        }
        catch
        {
            // Best-effort: failing to signal only means the supervisor may relaunch once.
        }
    }
}
