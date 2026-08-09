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
}
