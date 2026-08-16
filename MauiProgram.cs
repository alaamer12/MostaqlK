using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.LifecycleEvents;
using MostaqlK.Features.Notifications.ViewModels;
using MostaqlK.Features.Projects.ViewModels;
using MostaqlK.Features.Projects.Views;
using MostaqlK.Features.Settings.ViewModels;
using MostaqlK.Features.Settings.Views;
using MostaqlK.Infrastructure.Database;
using MostaqlK.Infrastructure.Http;
using MostaqlK.Infrastructure.Notifications;
using MostaqlK.Services;
using MostaqlK.Services.Pipeline;
using MostaqlK.Services.Pipeline.DiffEngine;
using MostaqlK.Services.Pipeline.WorkerPool;
using MostaqlK.UI.TrayIcon;
#if WINDOWS
using MostaqlK.Platforms.Windows;
#endif

namespace MostaqlK;

public static class MauiProgram
{
#if WINDOWS
	[System.Runtime.InteropServices.DllImport("dwmapi.dll")]
	private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

	/// <summary>
	/// Forces the window back to Windows' fully native, OS-drawn title bar and caption buttons
	/// (close/maximize/minimize), overriding MAUI's default of extending app content into the
	/// WinUI3 title bar (which draws its own custom-looking caption buttons instead), and keeps
	/// the title bar's colors in sync with the app's current light/dark theme.
	/// </summary>
	private static void RestoreNativeTitleBar(Microsoft.UI.Xaml.Window window)
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
	private static void ApplyTitleBarTheme(Microsoft.UI.Xaml.Window window)
	{
		var isDark = Microsoft.Maui.Controls.Application.Current?.RequestedTheme == Microsoft.Maui.ApplicationModel.AppTheme.Dark;

		var background = isDark
			? Windows.UI.Color.FromArgb(255, 30, 30, 30)
			: Windows.UI.Color.FromArgb(255, 255, 255, 255);
		var foreground = isDark
			? Windows.UI.Color.FromArgb(255, 255, 255, 255)
			: Windows.UI.Color.FromArgb(255, 0, 0, 0);
		var buttonHover = isDark
			? Windows.UI.Color.FromArgb(255, 60, 60, 60)
			: Windows.UI.Color.FromArgb(255, 230, 230, 230);
		var buttonPressed = isDark
			? Windows.UI.Color.FromArgb(255, 80, 80, 80)
			: Windows.UI.Color.FromArgb(255, 210, 210, 210);

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
	/// Hides the native scrollbar track/thumb on every <see cref="CollectionView"/> in the app
	/// (currently just the projects feed's list, <c>MainWindowPage.xaml</c>'s
	/// <c>Projects_ProjectsCollectionView</c>) without disabling scrolling itself - mouse-wheel
	/// and drag scrolling keep working, only the always-visible scrollbar chrome goes away, to
	/// match the flat, chrome-less list in projects.html. MAUI's CollectionView has no
	/// cross-platform "scrollbar visibility" property, so this reaches into the Windows handler's
	/// platform view (a WinUI <c>ListViewBase</c>, which owns an internal <c>ScrollViewer</c>) and
	/// sets the attached <c>ScrollViewer.VerticalScrollBarVisibility</c> property directly.
	/// </summary>
	private static void HideCollectionViewScrollBars()
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
#endif

	/// <summary>
	/// Best-effort safety net: logs otherwise-unobserved exceptions from background threads
	/// (pipeline loops, fire-and-forget tasks) instead of letting them surface as a silent,
	/// hard-to-diagnose process crash. This cannot catch native/WinRT fast-fail failures (like
	/// the dispatcher-teardown crash fixed via <c>App.RequestPipelineShutdown</c>), but it does
	/// stop ordinary unhandled managed exceptions from taking the whole app down ungracefully.
	/// </summary>
	private static void RegisterGlobalExceptionLogging()
	{
		AppDomain.CurrentDomain.UnhandledException += (_, args) =>
			MostaqlK.Services.Diagnostics.InteractionLogger.Fault(
				"AppDomain.UnhandledException",
				args.ExceptionObject as Exception ?? new Exception(args.ExceptionObject?.ToString() ?? "Unknown"));

		TaskScheduler.UnobservedTaskException += (_, args) =>
		{
			MostaqlK.Services.Diagnostics.InteractionLogger.Fault("TaskScheduler.UnobservedTaskException", args.Exception);
			args.SetObserved();
		};
	}

	public static MauiApp CreateMauiApp()
	{
		RegisterGlobalExceptionLogging();
#if WINDOWS
		HideCollectionViewScrollBars();
#endif

		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
				// Tajawal is the mockups' typeface (`font-family: 'Tajawal'` in every page in
				// .repertoire/design/mvp/). OpenSans has no Arabic coverage, so without these the
				// Arabic-first UI silently fell back to a system face and rendered noticeably
				// differently from the design.
				fonts.AddFont("Tajawal-Regular.ttf", "Tajawal");
				fonts.AddFont("Tajawal-Medium.ttf", "TajawalMedium");
				fonts.AddFont("Tajawal-Bold.ttf", "TajawalBold");
			});

#if DEBUG
		builder.Logging.AddDebug();
		builder.Logging.AddConsole();
#endif

		// Infrastructure
		// mostaql.com sits behind a bot filter that answers a *header-less* request with HTTP 403
		// before any HTML is produced. A bare `new HttpClient()` sends no User-Agent at all, so every
		// single poll cycle failed at the listing fetch - verified directly against the endpoint:
		// no User-Agent => 403 (118 bytes), a normal browser User-Agent => 200 (~165 KB). That is
		// why the pipeline looked dead while the "last scan" counter kept ticking.
		builder.Services.AddSingleton<HttpClient>(_ =>
		{
			var http = new HttpClient();
			http.DefaultRequestHeaders.UserAgent.ParseAdd(
				"Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36");
			http.DefaultRequestHeaders.Accept.ParseAdd(
				"text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
			http.DefaultRequestHeaders.AcceptLanguage.ParseAdd("ar,en-US;q=0.9,en;q=0.8");
			return http;
		});
		builder.Services.AddSingleton<SqliteConnectionFactory>();
		builder.Services.AddSingleton<IProjectScraper, MostaqlScraper>();
		builder.Services.AddSingleton<IProjectRepository, ProjectRepository>();
		builder.Services.AddSingleton<IOwnerRepository, OwnerRepository>();
		builder.Services.AddSingleton<IAssetRepository, AssetRepository>();
		builder.Services.AddSingleton<WindowsToastSender>();
		builder.Services.AddSingleton<Infrastructure.Database.SearchIndex.FtsQueryService>();
		builder.Services.AddSingleton<AssetDownloadService>();
		builder.Services.AddSingleton<DesignDataSeeder>();
		// Session cookie uploaded from Settings, held encrypted in `app_secrets` (DPAPI/CurrentUser)
		// and republished to CookieJar so the scraper/downloader can resolve real attachment URLs.
		builder.Services.AddSingleton<ISecretRepository, SecretRepository>();
		builder.Services.AddSingleton<CookieStore>();

		// Pipeline services
		builder.Services.AddSingleton<InFlightTracker>();
		builder.Services.AddSingleton<DiscoveryQueue>();
		// The shared budget is `max_requests_per_minute` (configuration-reference.md: default 2), and
		// the user's saved value must be honoured from the very first poll cycle - it used to be read
		// only by SettingsViewModel, which is Transient and never constructed unless the Settings page
		// was opened, so the running limiter always kept its hard-coded startup numbers.
		// `settings_safe_requests` (the "الطلبات الآمنة" checkbox) decides whether that budget is
		// enforced with the documented spacing or with the older, much faster burst behaviour.
		builder.Services.AddSingleton<TokenBucketRateLimiter>(_ => new TokenBucketRateLimiter(
			Microsoft.Maui.Storage.Preferences.Get(
				"settings_max_requests_per_minute",
				TokenBucketRateLimiter.DefaultRequestsPerMinute),
			Microsoft.Maui.Storage.Preferences.Get("settings_safe_requests", true)));
		builder.Services.AddSingleton<SqliteCommittedProvider>();
		builder.Services.AddSingleton<InFlightSetProvider>();
		builder.Services.AddSingleton<DiffEngine>();
		builder.Services.AddSingleton<IEnrichmentService, EnrichmentService>();
		builder.Services.AddSingleton<IPollService, PollService>();
		builder.Services.AddSingleton<WorkerPool>();

		// Notifications
		builder.Services.AddSingleton<NotificationGrouper>();
		builder.Services.AddSingleton<INotificationDispatcher, NotificationDispatcher>();

		// Tray icon
		builder.Services.AddSingleton<TrayIconService>();

		// X-button close-to-tray confirmation (Avast-style "keep running in background")
		builder.Services.AddSingleton<CloseBehaviorService>();

		// Global Status
		builder.Services.AddSingleton<GlobalAppStatusService>();
		builder.Services.AddSingleton<AppLifecycleService>();
		builder.Services.AddSingleton<PublishedTimeUpdateService>();

		// Features: Projects
		builder.Services.AddTransient<ProjectFeedViewModel>();
		builder.Services.AddTransient<StatusBarViewModel>();
		builder.Services.AddTransient<ProjectDetailsViewModel>();
		builder.Services.AddTransient<MainWindowPage>();
		builder.Services.AddTransient<AboutPage>();
		builder.Services.AddTransient<ProjectDetailsPage>();

		// Features: Notifications
		builder.Services.AddTransient<NotificationCenterViewModel>();

		// Features: Settings
		builder.Services.AddTransient<SettingsViewModel>();
		builder.Services.AddTransient<SettingsPanel>();

#if WINDOWS
		// Hosts the native Shell_NotifyIcon tray icon once the main WinUI window is created,
		// and tears it down when it closes. `appRef` is set right after Build() below; by the
		// time OnWindowCreated actually fires (after the app starts running), it is populated.
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
					// MAUI extends app content into the WinUI3 title bar by default, which makes
					// Windows draw its own custom caption buttons (close/maximize/minimize) using
					// the "Segoe Fluent Icons" font. That font ships built-in only on Windows 11 -
					// on Windows 10 (and other environments missing it) the buttons render as ugly
					// fallback glyphs with the minimize button missing entirely. Opting out here
					// restores the fully native, OS-drawn title bar and caption buttons, themed to
					// match the app's current light/dark mode.
					RestoreNativeTitleBar(window);
					// MAUI's own Window handler re-applies ExtendsContentIntoTitleBar after
					// OnWindowCreated fires (e.g. when the platform view finishes loading), so a
					// single assignment here can get silently overwritten. Re-assert once the
					// window is actually activated to make sure the native chrome sticks.
					window.Activated += (_, _) => RestoreNativeTitleBar(window);
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
					var trayIconService = appRef.Services.GetRequiredService<TrayIconService>();
					var appLifecycleService = appRef.Services.GetRequiredService<AppLifecycleService>();
					nativeHost = new TrayIconNativeHost(trayIconService, hwnd);

					var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
					var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);
					var closeBehaviorService = appRef.Services.GetRequiredService<CloseBehaviorService>();

					// Bring the window back from the tray when "Open" runs (sidebar/tray menu/tray
					// icon click), in case a previous X-button click hid it via MinimizeToTray below.
					trayIconService.RestoreRequested += () =>
						MainThread.BeginInvokeOnMainThread(() =>
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
								var (chosenAction, remember) = await CloseConfirmationDialog.ShowAsync(window);
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
					(Microsoft.Maui.Controls.Application.Current as App)?.RequestPipelineShutdown();
					nativeHost?.Dispose();
					nativeHost = null;
				}));
		});
#endif

		var app = builder.Build();

		// Resolve the cookie store immediately (it is what installs CookieJar.SecureProvider) and
		// load the stored session, so the first poll cycle is already authenticated rather than
		// silently anonymous until the user happens to open Settings.
		_ = app.Services.GetRequiredService<CookieStore>().InitializeAsync();

		// Start background time updates.
		app.Services.GetRequiredService<PublishedTimeUpdateService>().Start();

		// [TEMPORARY VERIFICATION] Export data to JSON after 70 seconds.
		_ = Task.Run(async () =>
		{
			await Task.Delay(10000); // Wait for first projects to be saved
			var repo = app.Services.GetRequiredService<IProjectRepository>();
			var recent = await repo.GetRecentAsync(50);
			if (recent.IsOk && recent.Value.Count > 0)
			{
				// Simulate old projects for testing the update service
				var projectToUpdate = recent.Value[0];
				using var connection = app.Services.GetRequiredService<SqliteConnectionFactory>().CreateConnection();
				using var cmd = connection.CreateCommand();
				cmd.CommandText = "UPDATE projects SET discovered_at = @date WHERE project_id = @id";
				cmd.Parameters.AddWithValue("@date", DateTimeOffset.UtcNow.AddHours(-2).AddMinutes(-5).ToString("O"));
				cmd.Parameters.AddWithValue("@id", projectToUpdate.ProjectId);
				cmd.ExecuteNonQuery();
			}

			await Task.Delay(65000); // Total ~75s, ensures PublishedTimeUpdateService runs at least once
			
			recent = await repo.GetRecentAsync(50);
			if (recent.IsOk)
			{
				var json = System.Text.Json.JsonSerializer.Serialize(recent.Value, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
				Directory.CreateDirectory("scratch");
				File.WriteAllText("scratch/exported_data.json", json);
				
				// Exit app after verification
				Environment.Exit(0);
			}
		});

#if WINDOWS
		appRef = app;
#endif

		return app;
	}
}
