using Microsoft.Maui.Controls;

namespace MostaqlK.Platforms.Windows;

/// <summary>
/// Windows-only window-sizing constants and the "AppButtonWindows" style-injection workaround,
/// extracted out of the shared <c>App.xaml.cs</c>. Neither has a mobile equivalent: Android/iOS
/// apps are always fullscreen (no window frame/caption to size around), and MAUI's own
/// <c>Style.BasedOn</c>/handler-mapping mechanism (not a dynamically-loaded resource dictionary)
/// is what mobile platforms should use for their own per-platform button tuning instead of this
/// workaround. See <c>cross-platform-ui-conventions.md</c>, Mechanism 1.
/// </summary>
internal static class AppWindowMetrics
{
    /// <summary>Height of the WinUI caption/title band that sits above the client area.</summary>
    public const int CaptionHeight = 32;

    /// <summary>
    /// Extra height WinUI silently takes off the requested <see cref="Microsoft.Maui.Controls.Window.Height"/> on Windows 11
    /// (the resize-frame inset is subtracted from the value MAUI forwards to the AppWindow). Measured
    /// by capture: requesting 832 produced an 824px frame, i.e. a 792px client area once the 32px
    /// caption band is cropped — the design-parity harness then padded the missing 8 rows with black,
    /// which read as an 8px global vertical shift against the 800px mockup viewport.
    /// </summary>
    public const int FrameInset = 8;

    /// <summary>Total chrome height (caption + frame inset) that must be added on top of a design's
    /// intended client-area height to get the <see cref="Microsoft.Maui.Controls.Window.Height"/> value to request on Windows.</summary>
    public const int ChromeHeight = CaptionHeight + FrameInset;

    /// <summary>
    /// Windows-specific style overrides (BasedOn AppButtonBase, etc.), merged only on the Windows
    /// target framework, per Mechanism 1 in cross-platform-ui-conventions.md. Built in code (not
    /// via a dynamically-loaded XAML file) because <c>ResourceDictionary.Source</c> runtime
    /// resolution is not supported under this project's SourceGen XAML inflator and was causing
    /// an unhandled native crash on startup before any window could appear.
    /// </summary>
    public static void ApplyButtonStyleOverrides(ResourceDictionary resources)
    {
        if (resources.TryGetValue("AppButtonBase", out var baseButtonStyleValue) && baseButtonStyleValue is Style baseButtonStyle)
        {
            var windowsButtonStyle = new Style(typeof(Button)) { BasedOn = baseButtonStyle };
            windowsButtonStyle.Setters.Add(new Setter { Property = Button.PaddingProperty, Value = new Thickness(16, 10) });
            windowsButtonStyle.Setters.Add(new Setter { Property = Button.FontSizeProperty, Value = 14 });
            resources.Add("AppButtonWindows", windowsButtonStyle);
        }
    }
}
