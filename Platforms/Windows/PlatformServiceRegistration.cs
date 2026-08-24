using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.LifecycleEvents;
using MostaqlK.Services;
using MostaqlK.Services.Onboarding;
using MostaqlK.UI.DesignSystem;
using MostaqlK.UI.TrayIcon;

namespace MostaqlK.Platforms.Windows;

/// <summary>
/// Windows-only startup wiring extracted out of the shared <c>MauiProgram.cs</c> entry point:
/// native title-bar chrome management and CollectionView scrollbar suppression. Neither has a
/// mobile equivalent (Android/iOS have no window chrome/title bar, and MAUI's CollectionView
/// already hides scrollbars by default on touch platforms), so this lives entirely under
/// <c>Platforms/Windows/</c> per <c>structure.md</c> instead of behind inline <c>#if WINDOWS</c>
/// blocks in the shared file. Moved verbatim — behavior is unchanged from before this refactor.
/// </summary>
internal static class PlatformServiceRegistration
{
    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    /// <summary>
    /// Forces the window back to Windows' fully native, OS-drawn title bar and caption buttons
    /// (close/maximize/minimize), overriding MAUI's default of extending app content into the
    /// WinUI3 title bar (which draws its own custom-looking caption buttons instead), and keeps
    /// the title bar's colors in sync with the app's current light/dark theme.
    /// </summary>
    public static void RestoreNativeTitleBar(Microsoft.UI.Xaml.Window window)
    {
        // With ExtendsContentIntoTitleBar = false, AppWindowTitleBar colors below only paint the
        // caption-BUTTON background, not the rest of the title-bar strip (that part stays the
        // OS non-client area, which can be forced dark by external theming tools regardless of
        // the app's own theme). Setting this to true hands the WHOLE strip over to WinUI/the
        // app's own background, so the recolor below covers the full width, not just the button
        // cluster - and native OS caption buttons (min/max/close) are still drawn by the OS on
        // the right-hand side of that strip, regardless of the app content's FlowDirection.
        window.ExtendsContentIntoTitleBar = true;

        ApplyTitleBarTheme(window);
    }

    /// <summary>
    /// Repaints the title-bar strip and native caption buttons to match the app's current
    /// effective theme (light or dark), instead of always forcing one fixed color. Called once
    /// when the window is created/activated and again whenever <c>AppTheme</c> changes (e.g. the
    /// user flips the dark-mode setting) so the title bar never gets out of sync with the rest
    /// of the UI.
    /// </summary>
    public static void ApplyTitleBarTheme(Microsoft.UI.Xaml.Window window)
    {
        var isDark = Microsoft.Maui.Controls.Application.Current?.RequestedTheme == Microsoft.Maui.ApplicationModel.AppTheme.Dark;

        var background = isDark
            ? global::Windows.UI.Color.FromArgb(255, 30, 30, 30)
            : global::Windows.UI.Color.FromArgb(255, 255, 255, 255);
        var foreground = isDark
            ? global::Windows.UI.Color.FromArgb(255, 255, 255, 255)
            : global::Windows.UI.Color.FromArgb(255, 0, 0, 0);
        var buttonHover = isDark
            ? global::Windows.UI.Color.FromArgb(255, 60, 60, 60)
            : global::Windows.UI.Color.FromArgb(255, 230, 230, 230);
        var buttonPressed = isDark
            ? global::Windows.UI.Color.FromArgb(255, 80, 80, 80)
            : global::Windows.UI.Color.FromArgb(255, 210, 210, 210);

        // The Page's own BackgroundColor only paints the Page's own content area; MAUI reserves
        // the title-bar-height strip above it as inset padding handled by the native WinUI
        // Window's own root panel (`window.Content`), which is a separate visual with its own
        // background. Paint that root panel to match the theme too, so nothing mismatched shows
        // through in the reserved strip behind the caption buttons.
        if (window.Content is Microsoft.UI.Xaml.Controls.Panel rootPanel)
        {
            rootPanel.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(background);
        }

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);
        // NOTE: SetBorderAndTitleBar(hasTitleBar: true) forces the classic OS-drawn non-client
        // title bar back on, which fights ExtendsContentIntoTitleBar = true above (the app is
        // supposed to own that area once extended) - do not call it while extending.

        // AppWindowTitleBar colors are drawn by WinUI itself (not the OS non-client painter), so
        // they are not subject to any external dark-theme override, and cover the full strip
        // once ExtendsContentIntoTitleBar is true above. Caption buttons stay OS-managed and
        // docked to the right by default - this only recolors them, it never reorders them.
        if (appWindow?.TitleBar is { } titleBar)
        {
            titleBar.BackgroundColor = background;
            titleBar.InactiveBackgroundColor = background;
            titleBar.ForegroundColor = foreground;
            titleBar.InactiveForegroundColor = foreground;
            titleBar.ButtonBackgroundColor = background;
            titleBar.ButtonInactiveBackgroundColor = background;
            titleBar.ButtonForegroundColor = foreground;
            titleBar.ButtonInactiveForegroundColor = foreground;
            titleBar.ButtonHoverBackgroundColor = buttonHover;
            titleBar.ButtonHoverForegroundColor = foreground;
            titleBar.ButtonPressedBackgroundColor = buttonPressed;
            titleBar.ButtonPressedForegroundColor = foreground;
        }

        // Keep the OS's own immersive-dark-mode flag in sync too (affects things like the
        // system context menu on the caption), even though the strip itself is now painted by
        // AppWindowTitleBar above.
        int useDarkMode = isDark ? 1 : 0;
        DwmSetWindowAttribute(hwnd, 20 /* DWMWA_USE_IMMERSIVE_DARK_MODE */, ref useDarkMode, sizeof(int));
        DwmSetWindowAttribute(hwnd, 19 /* DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 */, ref useDarkMode, sizeof(int));
    }

    /// <summary>
    /// Hides the native scrollbar track/thumb on every <see cref="Microsoft.Maui.Controls.CollectionView"/>
    /// in the app (currently just the projects feed's list, <c>MainWindowPage.xaml</c>'s
    /// <c>Projects_ProjectsCollectionView</c>) without disabling scrolling itself - mouse-wheel
    /// and drag scrolling keep working, only the always-visible scrollbar chrome goes away, to
    /// match the flat, chrome-less list in projects.html. MAUI's CollectionView has no
    /// cross-platform "scrollbar visibility" property, so this reaches into the Windows handler's
    /// platform view (a WinUI <c>ListViewBase</c>, which owns an internal <c>ScrollViewer</c>) and
    /// sets the attached <c>ScrollViewer.VerticalScrollBarVisibility</c> property directly. Not
    /// needed on mobile - MAUI's default touch-scrollbar behavior already matches the flat look.
    /// </summary>
    public static void HideCollectionViewScrollBars()
    {
        Microsoft.Maui.Controls.Handlers.Items.CollectionViewHandler.Mapper.AppendToMapping("HideScrollBar", (handler, _) =>
        {
            if (handler.PlatformView is Microsoft.UI.Xaml.Controls.ListViewBase listViewBase)
            {
                Microsoft.UI.Xaml.Controls.ScrollViewer.SetVerticalScrollBarVisibility(listViewBase, Microsoft.UI.Xaml.Controls.ScrollBarVisibility.Hidden);
                Microsoft.UI.Xaml.Controls.ScrollViewer.SetHorizontalScrollBarVisibility(listViewBase, Microsoft.UI.Xaml.Controls.ScrollBarVisibility.Hidden);
            }
        });
    }

    /// <summary>
    /// Wires up the native Shell_NotifyIcon tray icon once the main WinUI window is created, and
    /// tears it down when it closes — plus the Avast-style "keep running in background"
    /// close-to-tray confirmation flow (via <see cref="ExitConfirmationBox"/>/
    /// <see cref="CloseBehaviorService"/>) and native title-bar restoration. Moved verbatim out of
    /// <c>MauiProgram.cs</c>'s <c>ConfigureLifecycleEvents</c> call (previously a ~175-line inline
    /// <c>#if WINDOWS</c> block) per <c>cross-platform-ui-conventions.md</c>'s "no in-body
    /// <c>#if PLATFORM</c> outside <c>CurrentPlatform.cs</c>" rule — behavior is unchanged.
    /// <para>
    /// The returned delegate must be invoked once, right after <c>builder.Build()</c>, with the
    /// resulting <see cref="MauiApp"/> — the lifecycle callbacks below are registered before the
    /// app exists yet, but need it (for DI resolution) once <c>OnWindowCreated</c> actually fires.
    /// </para>
    /// </summary>
    public static Action<MauiApp> ConfigureWindowsLifecycleEvents(MauiAppBuilder builder)
    {
        // Hosts the native Shell_NotifyIcon tray icon once the main WinUI window is created,
        // and tears it down when it closes. `appRef` is set by the returned delegate right after
        // Build() below; by the time OnWindowCreated actually fires (after the app starts
        // running), it is populated.
        MauiApp? appRef = null;
        TrayIconNativeHost? nativeHost = null;
        // Guards the whole "close to tray vs. exit" flow against re-entrancy: AppWindow.Closing
        // can fire again while a previous ContentDialog await is still pending (e.g. the user
        // mashes the X button), and once the user has actually confirmed Exit, that decision
        // must stick for any further Closing callbacks instead of asking again.
        var isClosePromptShowing = false;
        var isExitConfirmed = false;

        builder.ConfigureLifecycleEvents(events =>
        {
            events.AddWindows(windows => windows
                .OnWindowCreated(window =>
                {
                    NativeSplashScreen.Hide();
                    // MAUI extends app content into the WinUI3 title bar by default, which makes
                    // Windows draw its own custom caption buttons (close/maximize/minimize) using
                    // the "Segoe Fluent Icons" font. That font ships built-in only on Windows 11 -
                    // on Windows 10 (and other environments missing it) the buttons render as ugly
                    // fallback glyphs with the minimize button missing entirely. Opting out here
                    // restores the fully native, OS-drawn title bar and caption buttons, themed to
                    // match the app's current light/dark mode.
                    RestoreNativeTitleBar(window);
                    if ((Microsoft.Maui.Controls.Application.Current as MostaqlK.App)?.IsOnboardingWindowPending == true)
                    {
                        var onboardingHwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
                        var onboardingId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(onboardingHwnd);
                        var onboardingAppWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(onboardingId);
                        if (onboardingAppWindow?.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
                        {
                            presenter.IsResizable = false;
                            presenter.IsMaximizable = false;

                            // FIX (main window stuck non-resizable after onboarding): this same
                            // native window is REUSED for the main shell once onboarding
                            // completes (App.OnboardingCompleted swaps window.Page instead of
                            // opening a brand-new window), so the presenter lockdown above must be
                            // undone once onboarding is actually done - otherwise the main window
                            // silently inherits the onboarding window's "not resizable/not
                            // maximizable" presenter forever.
                            if (appRef?.Services.GetService<OnboardingStateService>() is { } onboardingStateService)
                            {
                                onboardingStateService.Completed += (_, _) =>
                                    Microsoft.Maui.ApplicationModel.MainThread.BeginInvokeOnMainThread(() =>
                                    {
                                        presenter.IsResizable = true;
                                        presenter.IsMaximizable = true;
                                    });
                            }
                        }
                    }
                    if ((Microsoft.Maui.Controls.Application.Current as MostaqlK.App)?.IsOnboardingWindowPending == true)
                    {
                        return;
                    }
                    // MAUI's own Window handler re-applies ExtendsContentIntoTitleBar after
                    // OnWindowCreated fires (e.g. when the platform view finishes loading), so a
                    // single assignment here can get silently overwritten. Re-assert once the
                    // window is actually activated to make sure the native chrome sticks.
                    window.Activated += (_, _) =>
                    {
                        NativeSplashScreen.Hide();
                        RestoreNativeTitleBar(window);
                    };
                    // Keep the title bar colors in sync whenever the user switches the app's
                    // light/dark theme at runtime (e.g. via the Settings page's dark-mode toggle).
                    if (Microsoft.Maui.Controls.Application.Current is { } themedApp)
                    {
                        themedApp.RequestedThemeChanged += (_, _) => ApplyTitleBarTheme(window);
                    }

                    if (appRef is null)
                    {
                        return;
                    }

                    var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
                    // Resolved via PlatformCapability.WindowsOnly — null on non-Windows heads.
                    // On the Windows TFM this is always non-null; still null-check so the capability
                    // contract stays honest if a future path resolves without the tray.
                    var trayIconService = appRef.Services.GetService<TrayIconService>();
                    if (trayIconService is null)
                    {
                        return;
                    }

                    var appLifecycleService = appRef.Services.GetRequiredService<AppLifecycleService>();
                    nativeHost = new TrayIconNativeHost(trayIconService, hwnd);

                    var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
                    var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);
                    var closeBehaviorService = appRef.Services.GetRequiredService<CloseBehaviorService>();

                    // Bring the window back from the tray when "Open" runs (sidebar/tray menu/tray
                    // icon click), in case a previous X-button click hid it via MinimizeToTray below.
                    trayIconService.RestoreRequested += () =>
                        Microsoft.Maui.ApplicationModel.MainThread.BeginInvokeOnMainThread(() =>
                        {
                            appWindow.Show();
                            window.Activate();
                            appLifecycleService.IsInBackground = false;
                        });

                    // Avast-style "keep running in background": the X button no longer closes the
                    // process outright. AppWindow.Closing (unlike WinUI's plain Window.Closed) is
                    // cancelable, so every click is intercepted here first.
                    appWindow.Closing += async (sender, closingArgs) =>
                    {
                        if (isExitConfirmed)
                        {
                            // The user already chose "force closing" (directly, or via the
                            // remembered decision below) - let this and any further click close
                            // for real; nothing left to ask.
                            return;
                        }

                        // Idempotent guard: a second click while the ContentDialog await below is
                        // still pending must not stack a second dialog on top of the first.
                        closingArgs.Cancel = true;
                        if (isClosePromptShowing)
                        {
                            return;
                        }

                        var rememberedAction = closeBehaviorService.GetRememberedAction();
                        CloseAction action;
                        if (rememberedAction is { } remembered)
                        {
                            // "the click should be idempotent": once remembered, every subsequent
                            // X-button click silently repeats the same action, no dialog shown.
                            action = remembered;
                        }
                        else
                        {
                            isClosePromptShowing = true;
                            try
                            {
                                var (chosenAction, remember) = await ExitConfirmationBox.ShowAsync(window);
                                action = chosenAction;
                                if (remember)
                                {
                                    closeBehaviorService.RememberAction(action);
                                }
                            }
                            finally
                            {
                                isClosePromptShowing = false;
                            }
                        }

                        if (action == CloseAction.Exit)
                        {
                            isExitConfirmed = true;
                            appWindow.Destroy();
                        }
                        else
                        {
                            // Hide, not minimize: no taskbar entry, same as Avast - the pipeline
                            // (PollService/WorkerPool) keeps running untouched and the tray icon
                            // stays put; "Open" (tray menu/click) restores it via RestoreRequested.
                            appWindow.Hide();
                            appLifecycleService.IsInBackground = true;
                        }
                    };
                })
                .OnClosed((window, args) =>
                {
                    // FIX (fast-fail crash on the X button, exit code -1073741189 / 0xC000027B):
                    // stop the pipeline's background loops (PollService/WorkerPool) BEFORE the
                    // native window/dispatcher is fully torn down, so they cannot try to marshal
                    // another property change onto a dispatcher that no longer exists.
                    (Microsoft.Maui.Controls.Application.Current as MostaqlK.App)?.RequestPipelineShutdown();
                    nativeHost?.Dispose();
                    nativeHost = null;
                }));
        });

        return app => appRef = app;
    }
}
