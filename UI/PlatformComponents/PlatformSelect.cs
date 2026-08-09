namespace MostaqlK.UI.PlatformComponents;

/// <summary>
/// Central helper for Mechanism 1 ("same shape, per-OS tweaks") and for the rare runtime
/// (non-XAML-bindable) branch selection used by <c>UI/PlatformConcepts/</c>.
/// The build system picks the compiled branch via <c>#if</c> directives, so exactly one of the
/// supplied values is ever actually referenced at runtime for a given target framework.
/// </summary>
public static class PlatformSelect
{
    /// <summary>
    /// Resolves to the value supplied for the current compile-time target platform.
    /// Works both for plain value types and for <c>Func&lt;T&gt;</c> / view-returning lambdas —
    /// callers pass a factory delegate as <typeparamref name="T"/> when the resolved value is a
    /// MAUI <see cref="Microsoft.Maui.Controls.View"/> that should be constructed lazily.
    /// </summary>
    /// <typeparam name="T">The type of the value being selected (e.g. <c>Func&lt;View&gt;</c>).</typeparam>
    /// <param name="android">Value used when compiling for Android.</param>
    /// <param name="ios">Value used when compiling for iOS.</param>
    /// <param name="windows">Value used when compiling for Windows.</param>
    /// <param name="macCatalyst">Value used when compiling for Mac Catalyst.</param>
    /// <returns>The value matching the current target framework.</returns>
    public static T? For<T>(T? android = default, T? ios = default, T? windows = default, T? macCatalyst = default)
    {
#if ANDROID
        return android;
#elif IOS
        return ios;
#elif WINDOWS
        return windows;
#elif MACCATALYST
        return macCatalyst;
#else
        // No recognized platform target — fall back to whichever value was supplied first.
        // TODO: revisit once a headless/test target framework is introduced.
        return windows ?? android ?? ios ?? macCatalyst;
#endif
    }
}
