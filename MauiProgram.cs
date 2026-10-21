using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.LifecycleEvents;
using MostaqlK.Core.Platform;
using MostaqlK.Core.Sources;
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
using MostaqlK.Services.Diagnostics;
using MostaqlK.Services.Onboarding;
using MostaqlK.Services.Pipeline;
using MostaqlK.Sources.Mostaql;
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
    /// <see cref="CrashReporter.RegisterGlobalHandlers"/> is the single registration site and is
    /// idempotent, so the earlier call from <c>Platforms/Windows/Program.cs</c> is a no-op here.
    /// </summary>
    private static void RegisterGlobalExceptionLogging()
        => CrashReporter.RegisterGlobalHandlers();

    public static MauiApp CreateMauiApp()
    {
        AppPaths.Initialize();
        FilePreferences.Install();
        RegisterGlobalExceptionLogging();
#if WINDOWS
		MostaqlK.Platforms.Windows.PlatformServiceRegistration.HideCollectionViewScrollBars();
#endif

        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseSentry(options =>
            {
                options.Dsn = "https://90ccacc6ea89ac1af8c0f8cd27a97722@o4507036129361920.ingest.us.sentry.io/4512017976131584";
                options.Debug = false;
                options.Release = $"mostaqlk@{typeof(MauiProgram).Assembly.GetName().Version}";
#if DEBUG
				options.Environment = "debug";
#else
                options.Environment = "production";
#endif
                // Desktop app: one global scope for the whole process, not per async flow.
                options.IsGlobalModeEnabled = true;
                options.AttachStacktrace = true;
                options.MaxBreadcrumbs = 200;
                // Persist envelopes (and the open session) to disk before sending. A native fail-fast kills
                // the process instantly; without a disk cache anything still queued in memory is lost, and
                // the dead session can never be flagged. With it, the next launch uploads what was queued
                // and reports the killed session as abnormal (Release Health -> crash-free sessions).
                options.CacheDirectoryPath = Path.Combine(AppPaths.CacheDirectory, "sentry");
                options.AutoSessionTracking = true;
                // Gzipped WER minidumps forwarded post-mortem (Platforms/Windows/NativeCrashForwarder.cs).
                options.MaxAttachmentSize = 40 * 1024 * 1024;
            })
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
        builder.Services.AddSingleton<Microsoft.Maui.Storage.IPreferences>(FilePreferences.Default);
        // Per-source request headers come from the source descriptor via the policy factory, so the
        // bot-filter User-Agent (and its why) live in Sources/Mostaql rather than inline here. The
        // mostaql policy is seeded with the exact Chrome/126 + Accept + Accept-Language the app sent
        // before, so the bot-filter behavior is unchanged (see MostaqlSource for the full rationale).
        builder.Services.AddSingleton<IHttpClientPolicyFactory, HttpClientPolicyFactory>();
        builder.Services.AddSingleton<HttpClient>(sp =>
            sp.GetRequiredService<IHttpClientPolicyFactory>()
                .Create(sp.GetRequiredService<SourceRegistry>().Get(SourceId.Default)));
        builder.Services.AddSingleton<SqliteConnectionFactory>();
        // Source modules: each monitored website registers itself here. Adding a website
        // to the app = one more AddXxxSource() line + one module folder; nothing above or
        // below this block changes.
        builder.Services.AddMostaqlSource();
        builder.Services.AddSingleton<SourceRegistry>(sp =>
            new SourceRegistry(sp.GetServices<ISourceDescriptor>()));
        // One isolated cookie jar + one namespaced settings store per registered source.
        builder.Services.AddSingleton<ICookieJarProvider, CookieJarProvider>();
        builder.Services.AddSingleton<ISettingsRepository, FilePreferencesSettingsRepository>();
        // Live brand palette of whichever source owns the UI identity (SourceLinkButton et al.).
        builder.Services.AddSingleton<MostaqlK.UI.DesignSystem.BrandTokens.IBrandTokenProvider, MostaqlK.UI.DesignSystem.BrandTokens.SourceBrandTokenProvider>();
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
        // and republished into this source's cookie jar so the fetcher/downloader can resolve real
        // attachment URLs.
        builder.Services.AddSingleton<ISecretRepository, SecretRepository>();
        builder.Services.AddSingleton<CookieStore>();

        // Pipeline services: one full engine per registered source, assembled by the factory
        // and grouped by the orchestrator. The per-source components (queue, tracker, limiter,
        // poll loop, worker pool) are NOT registered globally - only the active pipeline's
        // instances are surfaced below, so the settings UI, status bar, feed, and tray keep
        // injecting the exact objects the running engine uses. Adding a source changes nothing
        // here: its pipeline appears next to the existing one with its own budget and telemetry.
        builder.Services.AddSingleton<SourcePipelineFactory>();
        builder.Services.AddSingleton<SourcePipelineOrchestrator>();
        builder.Services.AddSingleton(sp => sp.GetRequiredService<SourcePipelineOrchestrator>().Active.Queue);
        builder.Services.AddSingleton(sp => sp.GetRequiredService<SourcePipelineOrchestrator>().Active.Tracker);
        builder.Services.AddSingleton(sp => sp.GetRequiredService<SourcePipelineOrchestrator>().Active.Limiter);
        builder.Services.AddSingleton<IEnrichmentService>(sp => sp.GetRequiredService<SourcePipelineOrchestrator>().Active.Enrichment);
        builder.Services.AddSingleton<IPollService>(sp => sp.GetRequiredService<SourcePipelineOrchestrator>().Active.Poll);
        builder.Services.AddSingleton(sp => sp.GetRequiredService<SourcePipelineOrchestrator>().Active.Pool);
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
                    sp.GetRequiredService<DiscoveryQueue>(),
                    sp.GetRequiredService<GlobalAppStatusService>()))!);

#if WINDOWS
		builder.Services.AddSingleton<MostaqlK.Platforms.Windows.WindowsStartupService>();
#endif
        builder.Services.AddSingleton<IStartupService>(sp =>
            PlatformCapability<IStartupService>.Resolve(
#if WINDOWS
				windows: () => sp.GetRequiredService<MostaqlK.Platforms.Windows.WindowsStartupService>()
#endif
            ) ?? new NullStartupService());

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

        // Construct every source pipeline once, immediately after the container exists: this is
        // what attaches the per-source status objects to GlobalAppStatusService, and it must
        // happen before the first window binds to the projection.
        _ = app.Services.GetRequiredService<SourcePipelineOrchestrator>();

        // Resolve the cookie store immediately (it is what installs the decrypted header into this
        // source's cookie jar) and load the stored session, so the first poll cycle is already
        // authenticated rather than silently anonymous until the user happens to open Settings.
        _ = app.Services.GetRequiredService<CookieStore>().InitializeAsync();

#if WINDOWS
		setWindowsAppRef(app);
#endif

        return app;
    }
}
