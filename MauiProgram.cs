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
using Microsoft.UI;
using Microsoft.UI.Windowing;
using WinRT.Interop;
using System.Runtime.InteropServices;
#endif

namespace MostaqlK;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			});

#if DEBUG
		builder.Logging.AddDebug();
#endif

		// Infrastructure
		builder.Services.AddSingleton<HttpClient>();
		builder.Services.AddSingleton<SqliteConnectionFactory>();
		builder.Services.AddSingleton<IProjectScraper, MostaqlScraper>();
		builder.Services.AddSingleton<IProjectRepository, ProjectRepository>();
		builder.Services.AddSingleton<IOwnerRepository, OwnerRepository>();
		builder.Services.AddSingleton<IAssetRepository, AssetRepository>();
		builder.Services.AddSingleton<WindowsToastSender>();
		builder.Services.AddSingleton<Infrastructure.Database.SearchIndex.FtsQueryService>();
		builder.Services.AddSingleton<AssetDownloadService>();

		// Pipeline services
		builder.Services.AddSingleton<InFlightTracker>();
		builder.Services.AddSingleton<DiscoveryQueue>();
		builder.Services.AddSingleton<TokenBucketRateLimiter>(_ => new TokenBucketRateLimiter(capacity: 10, refillPerSecond: 1));
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
		builder.ConfigureLifecycleEvents(events =>
		{
			events.AddWindows(windows => windows
				.OnWindowCreated(window =>
				{
					if (appRef is null)
					{
						return;
					}

					var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
					ConfigureWindowsChrome(hwnd);
					var trayIconService = appRef.Services.GetRequiredService<TrayIconService>();
					nativeHost = new TrayIconNativeHost(trayIconService, hwnd);
				})
				.OnClosed((window, args) =>
				{
					nativeHost?.Dispose();
					nativeHost = null;
				}));
		});
#endif

		var app = builder.Build();

#if WINDOWS
		appRef = app;
#endif

		return app;
	}

#if WINDOWS
	private static void ConfigureWindowsChrome(nint hwnd)
	{
		var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
		var appWindow = AppWindow.GetFromWindowId(windowId);
		if (appWindow?.TitleBar is not AppWindowTitleBar titleBar)
		{
			return;
		}

		// Extend content into the title bar so we own the FULL strip (not just the caption
		// button cluster). Without this, the leftmost portion of the title-bar row (system
		// icon/menu area) is drawn by the OS with its own (often black/dark) background,
		// leaving a visible black remnant next to the light caption buttons.
		appWindow.TitleBar.ExtendsContentIntoTitleBar = true;

		titleBar.BackgroundColor = Windows.UI.Color.FromArgb(255, 255, 255, 255);
		titleBar.ForegroundColor = Windows.UI.Color.FromArgb(255, 15, 23, 42);
		titleBar.InactiveBackgroundColor = Windows.UI.Color.FromArgb(255, 255, 255, 255);
		titleBar.InactiveForegroundColor = Windows.UI.Color.FromArgb(255, 100, 116, 139);
		titleBar.ButtonBackgroundColor = Windows.UI.Color.FromArgb(255, 255, 255, 255);
		titleBar.ButtonForegroundColor = Windows.UI.Color.FromArgb(255, 15, 23, 42);
		titleBar.ButtonInactiveBackgroundColor = Windows.UI.Color.FromArgb(255, 255, 255, 255);
		titleBar.ButtonInactiveForegroundColor = Windows.UI.Color.FromArgb(255, 100, 116, 139);
		titleBar.ButtonHoverBackgroundColor = Windows.UI.Color.FromArgb(255, 241, 245, 249);
		titleBar.ButtonHoverForegroundColor = Windows.UI.Color.FromArgb(255, 15, 23, 42);
		titleBar.ButtonPressedBackgroundColor = Windows.UI.Color.FromArgb(255, 226, 232, 240);
		titleBar.ButtonPressedForegroundColor = Windows.UI.Color.FromArgb(255, 15, 23, 42);

		// AppWindowTitleBar customization above only recolors the caption *buttons*, not the
		// non-client caption strip drawn by DWM itself. When the OS is in dark mode, DWM still
		// paints that strip black regardless of the AppWindowTitleBar colors, producing the
		// black remnant reported by the user. Force DWM's own caption/text colors to match our
		// light title bar (Windows 11 22H2+, DWMWA_CAPTION_COLOR / DWMWA_TEXT_COLOR / DWMWA_USE_IMMERSIVE_DARK_MODE).
		const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
		const int DWMWA_CAPTION_COLOR = 35;
		const int DWMWA_TEXT_COLOR = 36;

		int useLightMode = 0; // 0 = disable dark mode -> light caption
		_ = DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useLightMode, sizeof(int));

		// COLORREF is 0x00BBGGRR.
		int captionColor = 0x00FFFFFF; // white
		_ = DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref captionColor, sizeof(int));

		int textColor = 0x002A170F; // ARGB(15,23,42) -> BGR 0x2A170F
		_ = DwmSetWindowAttribute(hwnd, DWMWA_TEXT_COLOR, ref textColor, sizeof(int));
	}

	[DllImport("dwmapi.dll", PreserveSig = true)]
	private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);
#endif
}
