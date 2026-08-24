using Microsoft.Extensions.DependencyInjection;
using MostaqlK.Infrastructure.Database;
using MostaqlK.Services;
using MostaqlK.Services.Pipeline;
using MostaqlK.Services.Pipeline.WorkerPool;
using MostaqlK.Services.Onboarding;
using MostaqlK.Features.Onboarding.Views;
using System.Linq;
using System.IO;
using Microsoft.Maui.ApplicationModel;

namespace MostaqlK;

public partial class App : Application
{
	// Window-chrome sizing constants (WinUI caption height/frame inset) used to live here as
	// Windows-only literals - moved to Platforms/Windows/AppWindowMetrics.cs since Android/iOS
	// windows are always fullscreen and have no equivalent concept (see
	// cross-platform-ui-conventions.md, Mechanism 1). CreateWindow/CreateMainWindow below add
	// AppWindowMetrics.ChromeHeight only under #if WINDOWS.

	private readonly CancellationTokenSource _pipelineCts = new();
	private readonly IServiceProvider _services;
	private Window? _onboardingWindow;
	private bool _mainWindowOpened;

	public App(IServiceProvider services)
	{
		_services = services;
		InitializeComponent();
		services.GetRequiredService<OnboardingStateService>().Completed += OnboardingCompleted;
		services.GetRequiredService<OnboardingStateService>().ApplySavedQuery();

		// Windows-specific style overrides (BasedOn AppButtonBase, etc.) are merged only on the
		// Windows target framework, per Mechanism 1 in cross-platform-ui-conventions.md. Built in
		// code (not via a dynamically-loaded XAML file) because `ResourceDictionary.Source` runtime
		// resolution is not supported under this project's SourceGen XAML inflator and was causing
		// an unhandled native crash on startup before any window could appear.
#if WINDOWS
		MostaqlK.Platforms.Windows.AppWindowMetrics.ApplyButtonStyleOverrides(Resources);
#endif

		// TODO(RTL): the Arabic-first FlowDirection switch (dir="rtl" in the mockups) hooks in here,
		// e.g. `MainPage.FlowDirection = FlowDirection.RightToLeft;` once the window/root view exists.

		// Apply the persisted dark-mode preference eagerly at startup. SettingsViewModel is
		// registered Transient and only constructed when the Settings page is opened, so without
		// this, UserAppTheme stays Unspecified and the app silently follows the OS theme instead
		// of the mockups' light-theme default (per projects.html, dark mode starts OFF).
		// A `--theme=light|dark` startup argument overrides the stored preference so each page can be
		// captured deterministically in both themes during design-parity verification.
		var onboardingIsPending = !services.GetRequiredService<OnboardingStateService>().IsCompleted;
		UserAppTheme = onboardingIsPending
			? StartupNavigation.ResolveExplicitTheme(Environment.GetCommandLineArgs())
			: StartupNavigation.ResolveTheme(
				Environment.GetCommandLineArgs(),
				Microsoft.Maui.Storage.Preferences.Get("settings_is_dark_mode", false));

#if WINDOWS
		// FIX ("not a single notification, ever since day one"): AUMID + Start Menu shortcut +
		// AppNotificationManager COM registration used to happen lazily, only the first time a
		// toast was actually sent (WindowsToastSender.EnsureRegistered, called from a background
		// worker thread possibly minutes after launch, per NotificationGrouper's default
		// EndOfMinute buffering). Registering here instead - as early as possible in the app's
		// own startup, well before any polling/enrichment work begins - matches Microsoft's
		// documented app-notifications flow (register before the app can process its own
		// activation args) and removes the "first toast of the whole app's life" race entirely.
		MostaqlK.Infrastructure.Notifications.WindowsToastSender.EnsureRegisteredEagerly();
#endif

		// `--seed-design-data` replaces the local store with the dataset the MVP mockups are drawn
		// against and latches `design_parity_mode` on; `--seed-design-data=off` clears the latch and
		// restores live polling. Seeding is awaited inline (a couple of local SQLite writes) so the
		// feed's first `LoadAsync` cannot observe a half-seeded store.
		var designDataMode = ApplyDesignDataArgument(services, Environment.GetCommandLineArgs());

		// FIX (design_parity_mode persistence trap): the pipeline must NEVER be permanently
		// disabled just because a *previous* launch happened to pass --seed-design-data. Only the
		// CURRENT launch's explicit request (this run's argv) is allowed to keep the pipeline
		// offline; a stale persisted preference from a prior run is informational only (surfaced to
		// the UI/log) and must not silently strand the shipped app on frozen seed data forever.
		var explicitlySeededThisLaunch = DesignDataSeeder.ParseArguments(Environment.GetCommandLineArgs()) == true;

		MostaqlK.Services.Diagnostics.InteractionLogger.Mark(
			"App.Startup.DesignParityMode",
			explicitlySeededThisLaunch ? "A" : "B",
			new { designDataMode, explicitlySeededThisLaunch });

		// MAUI has no ASP.NET-style `IHostedService`, so the pipeline subsystem (Poll Service +
		// Worker Pool) is started here as fire-and-forget background loops off the app's own
		// lifetime token. Both are registered as singletons in `MauiProgram`, so this simply
		// kicks off their already-implemented `StartAsync` loops once.
		// FIX (pipeline working before onboarding finished): background polling/enrichment must not
		// begin while the onboarding window is still up - the user hasn't chosen a query yet and the
		// Start/Pause button on the main page should still reflect its persisted default state the
		// moment it appears, not "already ticking" from work that began seconds earlier. So the
		// pipeline is only started once onboarding has actually completed; if it's still pending,
		// starting is deferred to the same `Completed` event that swaps in the main shell below.
		var onboardingStateService = services.GetRequiredService<OnboardingStateService>();
		if (onboardingStateService.IsCompleted)
		{
			StartPipeline(services, explicitlySeededThisLaunch);
		}
		else
		{
			onboardingStateService.Completed += (_, _) => StartPipeline(services, explicitlySeededThisLaunch);
		}

		HandleDebugJsonArgument(services, Environment.GetCommandLineArgs());
	}

	/// <summary>
	/// Starts the Poll Service + Worker Pool background loops. Only THIS launch's explicit
	/// --seed-design-data argument keeps the pipeline offline — a persisted preference from a
	/// previous run can no longer do so (see fix note above `explicitlySeededThisLaunch`).
	///
	/// FIX (whole-window freeze while a fresh database was being filled): this used to be called
	/// directly from the constructor, which runs on the UI thread and carries WinUI's
	/// SynchronizationContext. Calling StartAsync directly meant every `await` inside
	/// `PollService.RunLoopAsync` and inside each `EnrichmentWorker.RunAsync` resumed *on the UI
	/// thread* - so the ~165 KB listing parse, every detail-page parse and every SQLite write for
	/// the whole backlog executed on the dispatcher and the window stopped responding until the
	/// queue drained. `Task.Run` starts both loops on the thread pool with no captured context;
	/// UI-bound state still reaches the UI safely because `GlobalAppStatusService` marshals its own
	/// PropertyChanged.
	/// </summary>
	private void StartPipeline(IServiceProvider services, bool explicitlySeededThisLaunch)
	{
		if (!explicitlySeededThisLaunch)
		{
			var pollService = services.GetRequiredService<IPollService>();
			var workerPool = services.GetRequiredService<WorkerPool>();

			// The poll interval is persisted but was only ever read by the Transient
			// SettingsViewModel, so an untouched app ignored the user's saved value entirely.
			pollService.PollIntervalSeconds = Math.Clamp(
				Microsoft.Maui.Storage.Preferences.Get("settings_poll_interval_seconds", pollService.PollIntervalSeconds),
				10,
				3600);

			// Persist the Start/Pause state: the app now "reserves the state it closed with".
			// Defaults to false (paused) on the very first run to satisfy the "first run not activated" requirement.
			var isPollingActive = Microsoft.Maui.Storage.Preferences.Get("settings_is_polling_active", false);
			pollService.SetPaused(!isPollingActive);

			// If debug-via-json is requested, force-enable polling to ensure we have data to export.
			if (Environment.GetCommandLineArgs().Contains("--debug-via-json"))
			{
				pollService.SetPaused(false);
			}

			var pipelineToken = _pipelineCts.Token;
			_ = Task.Run(() => pollService.StartAsync(pipelineToken), pipelineToken);
			_ = Task.Run(() => workerPool.StartAsync(pipelineToken), pipelineToken);
			MostaqlK.Services.Diagnostics.InteractionLogger.Mark("App.Startup.PipelineStarted", "A");
		}
		else
		{
			MostaqlK.Services.Diagnostics.InteractionLogger.Mark("App.Startup.PipelineSkipped", "B");
		}
	}

    private void HandleDebugJsonArgument(IServiceProvider services, string[] args)
    {
#if DEBUG
		if (!args.Contains("--debug-via-json")) return;

		Task.Run(async () =>
		{
			// Wait for 30 seconds to allow polling and enrichment to happen.
			await Task.Delay(30000);

			var projectRepo = services.GetRequiredService<IProjectRepository>();
			var ownerRepo = services.GetRequiredService<IOwnerRepository>();

			var projectsResult = await projectRepo.GetAllDetailsAsync();
			var ownersResult = await ownerRepo.GetAllAsync();

			if (projectsResult.IsOk && ownersResult.IsOk)
			{
				var exportData = new
				{
					ExportedAt = DateTimeOffset.UtcNow,
					Projects = projectsResult.Value,
					Owners = ownersResult.Value
				};

				var json = System.Text.Json.JsonSerializer.Serialize(exportData, new System.Text.Json.JsonSerializerOptions
				{
					WriteIndented = true
				});

				var scratchPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "scratch");
				if (!Directory.Exists(scratchPath)) Directory.CreateDirectory(scratchPath);

				var filePath = Path.Combine(scratchPath, "exported_data.json");
				await File.WriteAllTextAsync(filePath, json);

				MostaqlK.Services.Diagnostics.InteractionLogger.Mark("App.Debug.JsonExported", "A", new { filePath });
			}

			// Shutdown the app after export.
			MainThread.BeginInvokeOnMainThread(() =>
			{
				Application.Current?.Quit();
			});
		});
#endif
	}

	/// <summary>
	/// Handles the <c>--seed-design-data[=off]</c> startup argument and returns whether the app is
	/// currently running against seeded design-parity data (this launch's request if provided,
	/// otherwise the persisted preference — for display/logging purposes only; see the
	/// <c>explicitlySeededThisLaunch</c> check in the constructor for the actual pipeline gate).
	/// <c>--seed-design-data=off</c> now also purges any leftover seed-shaped rows through
	/// <see cref="DesignDataSeeder.PurgeSeededRowsAsync"/>, so turning the flag off on a store that
	/// was previously mixed with seed data actually leaves it clean instead of merely flipping the
	/// preference back off.
	/// </summary>
	private static bool ApplyDesignDataArgument(IServiceProvider services, string[] args)
	{
		var requested = DesignDataSeeder.ParseArguments(args);
		if (requested is null)
		{
			return Microsoft.Maui.Storage.Preferences.Get(DesignDataSeeder.PreferenceKey, false);
		}

		var seeder = services.GetRequiredService<DesignDataSeeder>();
		if (requested.Value)
		{
			seeder.SeedAsync().GetAwaiter().GetResult();
		}
		else
		{
			var purgeResult = seeder.PurgeSeededRowsAsync().GetAwaiter().GetResult();
			MostaqlK.Services.Diagnostics.InteractionLogger.Mark(
				"App.Startup.DesignDataPurge",
				purgeResult.IsOk ? "A" : "B",
				purgeResult.IsOk ? new { purgedProjectRows = purgeResult.Value } : null);
		}

		Microsoft.Maui.Storage.Preferences.Set(DesignDataSeeder.PreferenceKey, requested.Value);
		return requested.Value;
	}

	/// <summary>
	/// Stops the pipeline's background loops (Poll Service + Worker Pool) as soon as the window
	/// starts closing. Called from <see cref="MauiProgram"/>'s <c>OnClosed</c> lifecycle event.
	/// FIX (graceful shutdown, exit code -1073741189 / 0xC000027B on the X button): without this,
	/// those loops kept running on the thread pool after the native window/dispatcher was torn
	/// down, and their next attempt to marshal a property change onto the (now gone) UI dispatcher
	/// could fast-fail the whole process instead of exiting cleanly.
	/// </summary>
	internal void RequestPipelineShutdown()
	{
		if (!_pipelineCts.IsCancellationRequested)
		{
			MostaqlK.Services.Diagnostics.InteractionLogger.Mark("App.Shutdown.PipelineCancelled", "A");
			_pipelineCts.Cancel();
		}
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		var onboarding = _services.GetRequiredService<OnboardingStateService>();
		if (!onboarding.IsCompleted)
		{
			_onboardingWindow = new Window(_services.GetRequiredService<OnboardingPage>());
			var display = DeviceDisplay.MainDisplayInfo;
			var workWidth = display.Width / display.Density;
			var workHeight = display.Height / display.Density;
			var scale = Math.Clamp(Math.Min((workWidth - 48) / 920d, (workHeight - 96) / 720d), 0.65d, 1.25d);
			_onboardingWindow.Width = Math.Round(920 * scale);
			var onboardingHeight = Math.Round(720 * scale);
#if WINDOWS
			onboardingHeight += MostaqlK.Platforms.Windows.AppWindowMetrics.ChromeHeight;
#endif
			_onboardingWindow.Height = onboardingHeight;
			return _onboardingWindow;
		}

		return CreateMainWindow();
	}

	private Window CreateMainWindow()
	{
		var window = new Window(new AppShell(StartupNavigation.FromArguments(Environment.GetCommandLineArgs())));

		// The mockups are authored against a fixed 1280x800 desktop viewport, so the window opens
		// sized so its *client* area is exactly that: `Window.Height` on Windows covers the whole
		// frame, so the 32px caption band is added on top. This keeps design-parity captures
		// deterministic instead of depending on whatever size the OS last remembered.
		window.Width = 1280;
		double mainHeight = 800;
#if WINDOWS
		mainHeight += MostaqlK.Platforms.Windows.AppWindowMetrics.ChromeHeight;
#endif
		window.Height = mainHeight;

		return window;
	}

	internal bool IsOnboardingWindowPending => _onboardingWindow is not null && !_mainWindowOpened;

	private void OnboardingCompleted(object? sender, EventArgs e)
	{
		if (_mainWindowOpened)
		{
			return;
		}

		_mainWindowOpened = true;
		MainThread.BeginInvokeOnMainThread(() =>
		{
			// FIX (WinUI window swap crash 0xc000027b): instead of closing the onboarding window
			// and opening a new one (which triggers process shutdown in MAUI WinUI), we
			// REUSE the existing window and swap its root content.
			if (_onboardingWindow is { } window)
			{
				var shell = new AppShell(StartupNavigation.FromArguments(Environment.GetCommandLineArgs()));
				
				// Pre-size the window to the main shell's expected dimensions before swapping content
				window.Width = 1280;
				double swapHeight = 800;
#if WINDOWS
				swapHeight += MostaqlK.Platforms.Windows.AppWindowMetrics.ChromeHeight;
#endif
				window.Height = swapHeight;
				
				// Swap the content
				window.Page = shell;
			}
			else
			{
				// Fallback if window was somehow not tracked
				var mainWindow = CreateMainWindow();
				OpenWindow(mainWindow);
			}
		});
	}
}
