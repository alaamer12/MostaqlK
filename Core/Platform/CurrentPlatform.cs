namespace MostaqlK.Core.Platform;

/// <summary>
/// The set of platforms MostaqlK can run on. Intentionally small and closed — new members are
/// only added when a real <c>Platforms/&lt;Name&gt;/</c> folder is introduced for that target.
/// </summary>
public enum AppPlatform
{
    Windows,
    Android,
    iOS,
    MacCatalyst,
}

/// <summary>
/// Single, canonical place to read "what platform is this build running on". Every
/// capability-mapping call site (see <see cref="PlatformCapability{T}"/>) and every future
/// cross-platform audit should read the platform from here instead of re-checking
/// <c>#if WINDOWS</c>/<c>DeviceInfo.Platform</c> ad hoc at each site.
/// <para>
/// The running platform is fixed for the entire lifetime of the process — a launched app
/// instance is compiled for exactly one target framework and can never "become" another platform
/// while running. <see cref="Current"/> is therefore evaluated once (compile-time via <c>#if</c>)
/// and never re-detected at runtime.
/// </para>
/// </summary>
public static class CurrentPlatform
{
    /// <summary>The platform this build was compiled for. Fixed for the process's lifetime.</summary>
    public static AppPlatform Current { get; } =
#if WINDOWS
        AppPlatform.Windows;
#elif ANDROID
        AppPlatform.Android;
#elif IOS
        AppPlatform.iOS;
#elif MACCATALYST
        AppPlatform.MacCatalyst;
#else
        AppPlatform.Windows; // Fallback for non-MAUI-head contexts (e.g. unit tests); revisit if a headless TFM is added.
#endif

    /// <summary>True when running on the Windows desktop target.</summary>
    public static bool IsWindows => Current == AppPlatform.Windows;

    /// <summary>True when running on a touch/mobile target (Android or iOS).</summary>
    public static bool IsMobile => Current is AppPlatform.Android or AppPlatform.iOS;
}
