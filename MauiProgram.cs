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
using MostaqlK.Sources.Nafezly;
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
                options.Release = $"mostaqlk{ChannelIdentity.Suffix.ToLowerInvariant()}@{typeof(MauiProgram).Assembly.GetName().Version}";
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
        builder.Services.AddNafezlySource();
        builder.Services.AddSingleton<SourceRegistry>(sp =>
            new SourceRegistry(sp.GetServices<ISourceDescriptor>()));
        // Index of every per-source contribution (settings sections, pages) built from the DI
        // collections, mirroring SourceRegistry. Adding a source's contribution is only a module-side
        // registration — the shared UI asks the registry for the ACTIVE source's sections and never
        // branches on a source id (contract §10 "growth without branches").
        builder.Services.AddSingleton<SourceContributionRegistry>(sp =>
            new SourceContributionRegistry(
                sp.GetServices<ISourcePageContribution>(),
                sp.GetServices<ISourceSettingsContribution>()));
        // One isolated cookie jar + one namespaced settings store per registered source.
        builder.Services.AddSingleton<ICookieJarProvider, CookieJarProvider>();
        builder.Services.AddSingleton<ISettingsRepository, FilePreferencesSettingsRepository>();
        // Which platforms are switched on, and the at-least-one-enabled invariant. One instance for
        // the settings row, the pipeline start-up, and the toast dispatcher — the rule must not be
        // decided three ways.
        builder.Services.AddSingleton<PlatformEnablement>();
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
        // v3 frame-decoupled read model (plan Step 1): the ONLY thing the feed renders from. It
        // runs the existing bounded repository queries off the click path, coalesces bursts, and
        // pointer-swaps immutable per-source snapshots; SnapshotUpdated is marshalled to the UI
        // dispatcher. Registration line only — the store itself lives in Services/SourceFeedStore.cs.
        builder.Services.AddSingleton<SourceFeedStore>(sp => new SourceFeedStore(
            new RepositorySourceFeedDataProvider(
                sp.GetRequiredService<IProjectRepository>(),
                (source, query, limit, ct) => sp.GetRequiredService<Infrastructure.Database.SearchIndex.FtsQueryService>()
                    .SearchAsync(source, query, limit, ct)),
            sp.GetRequiredService<SourceRegistry>().All.Select(descriptor => descriptor.Id).ToArray(),
            marshal: static action =>
            {
                // Same teardown-guarded dispatcher pattern as GlobalAppStatusService: a post after
                // the WinUI dispatcher is gone must fail soft, never fast-fail the process.
                try
                {
                    var dispatcher = Application.Current?.Dispatcher;
                    if (dispatcher is null)
                    {
                        action();
                        return;
                    }

                    dispatcher.Dispatch(action);
                }
                catch (Exception ex)
                {
                    MostaqlK.Services.Diagnostics.InteractionLogger.Fault("SourceFeedStore.Marshal.DispatchFailed", ex);
                }
            },
            faultSink: static (label, ex) =>
            {
                if (ex is not null)
                {
                    MostaqlK.Services.Diagnostics.InteractionLogger.Fault(label, ex);
                }
            }));
        // Deferred preference/settings writes (plan Steps 2-3): report background write faults the
        // same way; teardown flush is wired by the app host.
        MostaqlK.Core.Platform.DeferredPreferences.FaultSink = static (label, ex) =>
        {
            if (ex is not null)
            {
                MostaqlK.Services.Diagnostics.InteractionLogger.Fault(label, ex);
            }
        };
        builder.Services.AddSingleton<AssetDownloadService>();
        builder.Services.AddSingleton<DesignDataSeeder>();
        // Session cookie uploaded from Settings, held encrypted in `app_secrets` (DPAPI/CurrentUser)
        // and republished into this source's cookie jar so the fetcher/downloader can resolve real
        // attachment URLs.
        builder.Services.AddSingleton<ISecretRepository, SecretRepository>();
        // Per-source session stores (one CookieStore per descriptor), handed out by source so the
        // Settings cookie card manages the ACTIVE source's session instead of a single startup-bound
        // one. Mirrors ICookieJarProvider; each store installs into its own source's jar (contract §5/§10).
        builder.Services.AddSingleton<ICookieStoreProvider, CookieStoreProvider>();

        // Pipeline services: one full engine per registered source, assembled by the factory
        // and grouped by the orchestrator. The per-source components (queue, tracker, limiter,
        // poll loop, worker pool) are registered as TRANSIENT live-facade resolutions of the
        // CURRENTLY ACTIVE pipeline: transient UI VMs (settings, feed, status bar) reconstruct per
        // opening and thereby re-point automatically on a source morph, while singleton hosts
        // (tray, dispatcher) capture the active instances at their own construction, exactly as
        // before. Object identity is unchanged while the active source is stable — same engine,
        // same budget, same telemetry; only a switch changes what a fresh resolution sees.
        builder.Services.AddSingleton<SourcePipelineFactory>();
        builder.Services.AddSingleton<SourcePipelineOrchestrator>();
        builder.Services.AddSingleton<ActivePipeline>();
        builder.Services.AddTransient(sp => sp.GetRequiredService<ActivePipeline>().Queue);
        builder.Services.AddTransient(sp => sp.GetRequiredService<ActivePipeline>().Tracker);
        builder.Services.AddTransient(sp => sp.GetRequiredService<ActivePipeline>().Limiter);
        builder.Services.AddTransient<IEnrichmentService>(sp => sp.GetRequiredService<ActivePipeline>().Enrichment);
        builder.Services.AddTransient<IPollService>(sp => sp.GetRequiredService<ActivePipeline>().Poll);
        builder.Services.AddTransient(sp => sp.GetRequiredService<ActivePipeline>().Pool);
        builder.Services.AddSingleton<OnboardingStateService>();

        // Notifications
        // The grouper is ONE app-wide instance driven by SYSTEM settings, so it is seeded from the
        // store at construction. It used to start with Enabled=false and be told otherwise only when
        // the settings page happened to be constructed, which meant grouping was off after every fresh
        // launch no matter what the user had configured — the same class of bug as the worker pool
        // counting itself until someone opened Settings. Defaults here are the catalog's own literals
        // (TestSettingsDefaultsAgreeWithServices pins the catalog side; the harvest guarantees the flat
        // container already holds whatever an existing install had stored per platform).
        builder.Services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<ISettingsRepository>();
            var threshold = Math.Max(1, settings.GetGlobal(SettingsKeys.GroupingThreshold, 5));
            var grouper = new NotificationGrouper
            {
                Enabled = settings.GetGlobal(SettingsKeys.NotificationGroupingEnabled, true),
                Mode = Enum.TryParse<NotificationGroupingMode>(
                    settings.GetGlobal(SettingsKeys.GroupingMode, nameof(NotificationGroupingMode.EndOfMinute)), out var mode)
                    ? mode
                    : NotificationGroupingMode.EndOfMinute,
                AfterCountThreshold = threshold,
                AfterMinutesThreshold = threshold,
            };

            // Logged because "grouping silently off after a fresh launch" was the bug: the singleton's
            // start-up state is now whatever the store says, and this line is the evidence it did.
            InteractionLogger.Mark("NotificationGrouper.Seeded", "A", new
            {
                grouper.Enabled,
                Mode = grouper.Mode.ToString(),
                grouper.AfterCountThreshold,
            });
            return grouper;
        });
        builder.Services.AddSingleton<INotificationDispatcher, NotificationDispatcher>();

        // Tray icon — Windows-only desktop capability. PlatformCapability makes the
        // "null on mobile" answer explicit and typed; Windows behavior is unchanged.
        builder.Services.AddSingleton(sp =>
            PlatformCapability<TrayIconService>.WindowsOnly(() =>
                new TrayIconService(
                    sp.GetRequiredService<ActivePipeline>(),
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

        // Resolve the per-source cookie stores immediately (they are what install the decrypted
        // header into each source's cookie jar) and load every stored session, so the first poll
        // cycle of each pipeline is already authenticated rather than silently anonymous until the
        // user happens to open Settings.
        _ = app.Services.GetRequiredService<ICookieStoreProvider>().InitializeAllAsync();

#if WINDOWS
		setWindowsAppRef(app);
#endif

        return app;
    }
}
