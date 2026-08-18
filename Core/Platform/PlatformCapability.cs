using System;

namespace MostaqlK.Core.Platform;

/// <summary>
/// Declares, once, what a Windows-only-shaped capability resolves to on every platform —
/// including the explicit "this platform has no answer for this at all" case.
/// <para>
/// This generalizes the notification/tray-icon problem: some capabilities need a genuinely
/// different implementation per platform (e.g. notifications), while others simply don't exist
/// on mobile at all (e.g. the system tray icon). Both cases are expressed the same way here,
/// so call sites never hand-roll their own <c>#if WINDOWS</c>/null-check.
/// </para>
/// <example>
/// <code>
/// ITrayIconCapability? tray = PlatformCapability&lt;ITrayIconCapability&gt;.Resolve(
///     windows: () => new TrayIconService(...));
/// // tray is null on Android/iOS/MacCatalyst — callers must null-check/no-op.
/// </code>
/// </example>
/// </summary>
/// <typeparam name="T">The capability's abstraction type (usually an interface).</typeparam>
public static class PlatformCapability<T>
    where T : class
{
    /// <summary>
    /// Resolves the capability for <see cref="CurrentPlatform.Current"/>. Any platform argument
    /// left <see langword="null"/> means "not available on this platform" and callers receive a
    /// typed <see langword="null"/> instead of the capability being constructed/called at all.
    /// </summary>
    /// <param name="windows">Factory used on Windows, or <see langword="null"/> if unavailable there.</param>
    /// <param name="android">Factory used on Android, or <see langword="null"/> if unavailable there.</param>
    /// <param name="ios">Factory used on iOS, or <see langword="null"/> if unavailable there.</param>
    /// <param name="macCatalyst">Factory used on Mac Catalyst, or <see langword="null"/> if unavailable there.</param>
    public static T? Resolve(
        Func<T>? windows = null,
        Func<T>? android = null,
        Func<T>? ios = null,
        Func<T>? macCatalyst = null)
    {
        var factory = CurrentPlatform.Current switch
        {
            AppPlatform.Windows => windows,
            AppPlatform.Android => android,
            AppPlatform.iOS => ios,
            AppPlatform.MacCatalyst => macCatalyst,
            _ => null,
        };

        return factory?.Invoke();
    }

    /// <summary>
    /// Convenience overload for capabilities that are only implemented on Windows and have no
    /// mobile equivalent whatsoever (e.g. the tray icon). Equivalent to
    /// <c>Resolve(windows: windows)</c> with every other platform explicitly resolving to <see langword="null"/>.
    /// </summary>
    public static T? WindowsOnly(Func<T> windows) => Resolve(windows: windows);
}
