using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.LifecycleEvents;
using MostaqlK.Core.Platform;
using MostaqlK.Features.Notifications.ViewModels;
using MostaqlK.Features.Onboarding.ViewModels;
using MostaqlK.Features.Onboarding.Views;
using MostaqlK.Features.Projects.ViewModels;
using MostaqlK.Features.Projects.Views;
using MostaqlK.Features.Settings.ViewModels;
using MostaqlK.Features.Settings.Views;
using MostaqlK.Infrastructure.Database;
using MostaqlK.Infrastructure.Http;
using MostaqlK.Infrastructure.Notifications;
using MostaqlK.Services;
using MostaqlK.Services.Onboarding;
using MostaqlK.Services.Pipeline;
using MostaqlK.Services.Pipeline.DiffEngine;
using MostaqlK.Services.Pipeline.WorkerPool;
using MostaqlK.UI.DesignSystem;
using MostaqlK.UI.TrayIcon;
#if WINDOWS
using MostaqlK.Platforms.Windows;
#endif

namespace MostaqlK;

public static class MauiProgram
{
	// Native title-bar chrome management and CollectionView scrollbar suppression used to live
	// here behind #if WINDOWS - moved to Platforms/Windows/PlatformServiceRegistration.cs (see
	// cross-platform-ui-conventions.md, Mechanism 1) since neither has a mobile equivalent and
	// this shared entry-point file should not carry native WinUI code inline. Behavior unchanged.

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
		AppPaths.Initialize();
		RegisterGlobalExceptionLogging();
#if WINDOWS
		MostaqlK.Platforms.Windows.PlatformServiceRegistration.HideCollectionViewScrollBars();
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
		// Cross-platform note: this User-Agent impersonates a Windows desktop Chrome browser to
		// satisfy mostaql.com's bot filter (see comment above) - it describes what the SCRAPED
		// SITE sees, not the OS this app actually runs on, so it is intentionally identical on
		// every target (Android/iOS included) and does not need per-platform extraction.
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
#if WINDOWS
		// Windows-only toast delivery backend. The dispatcher depends on INotificationSender;
		// a non-Windows registration is future Android work, not required for V1 shipping.
		builder.Services.AddSingleton<INotificationSender, WindowsToastSender>();
#endif
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
		builder.Services.AddSingleton<OnboardingStateService>();

		// Notifications
		builder.Services.AddSingleton<NotificationGrouper>();
		builder.Services.AddSingleton<INotificationDispatcher, NotificationDispatcher>();

		// Tray icon — Windows-only desktop capability. PlatformCapability makes the
		// "null on mobile" answer explicit and typed; Windows behavior is unchanged.
		builder.Services.AddSingleton(sp =>
			PlatformCapability<TrayIconService>.WindowsOnly(() =>
				new TrayIconService(
					sp.GetRequiredService<IPollService>(),
					sp.GetRequiredService<DiscoveryQueue>()))!);

		// X-button close-to-tray confirmation (Avast-style "keep running in background")
		builder.Services.AddSingleton<CloseBehaviorService>();

		// Global Status
		builder.Services.AddSingleton<GlobalAppStatusService>();
		builder.Services.AddSingleton<AppLifecycleService>();

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

		// Features: Onboarding
		builder.Services.AddTransient<OnboardingViewModel>();
		builder.Services.AddTransient<OnboardingPage>();

#if WINDOWS
		// See Platforms/Windows/PlatformServiceRegistration.cs for the tray icon/close-to-tray/
		// title-bar lifecycle wiring itself — kept out of this shared entry-point file per
		// cross-platform-ui-conventions.md's "no in-body #if PLATFORM outside CurrentPlatform.cs"
		// rule. `setWindowsAppRef` must be invoked once, right after builder.Build() below.
		var setWindowsAppRef = MostaqlK.Platforms.Windows.PlatformServiceRegistration.ConfigureWindowsLifecycleEvents(builder);
#endif

		var app = builder.Build();

		// Resolve the cookie store immediately (it is what installs CookieJar.SecureProvider) and
		// load the stored session, so the first poll cycle is already authenticated rather than
		// silently anonymous until the user happens to open Settings.
		_ = app.Services.GetRequiredService<CookieStore>().InitializeAsync();

#if WINDOWS
		setWindowsAppRef(app);
#endif

		return app;
	}
}
