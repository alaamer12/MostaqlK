using Microsoft.Extensions.DependencyInjection;
using MostaqlK.Infrastructure.Database;
using MostaqlK.Services;
using MostaqlK.Services.Pipeline;
using MostaqlK.Services.Pipeline.WorkerPool;
using System.Linq;
using System.IO;
using Microsoft.Maui.ApplicationModel;

namespace MostaqlK;

public partial class App : Application
{
	/// <summary>Height of the WinUI caption/title band that sits above the client area.</summary>
	private const int WindowsCaptionHeight = 32;

	/// <summary>
	/// Extra height WinUI silently takes off the requested <see cref="Window.Height"/> on Windows 11
	/// (the resize-frame inset is subtracted from the value MAUI forwards to the AppWindow). Measured
	/// by capture: requesting 832 produced an 824px frame, i.e. a 792px client area once the 32px
	/// caption band is cropped — the design-parity harness then padded the missing 8 rows with black,
	/// which read as an 8px global vertical shift against the 800px mockup viewport.
	/// </summary>
	private const int WindowsFrameInset = 8;

	private readonly CancellationTokenSource _pipelineCts = new();

	public App(IServiceProvider services)
	{
		InitializeComponent();

		// Windows-specific style overrides (BasedOn AppButtonBase, etc.) are merged only on the
		// Windows target framework, per Mechanism 1 in cross-platform-ui-conventions.md. Built in
		// code (not via a dynamically-loaded XAML file) because `ResourceDictionary.Source` runtime
		// resolution is not supported under this project's SourceGen XAML inflator and was causing
		// an unhandled native crash on startup before any window could appear.
#if WINDOWS
		if (Resources.TryGetValue("AppButtonBase", out var baseButtonStyleValue) && baseButtonStyleValue is Style baseButtonStyle)
		{
			var windowsButtonStyle = new Style(typeof(Microsoft.Maui.Controls.Button)) { BasedOn = baseButtonStyle };
			windowsButtonStyle.Setters.Add(new Setter { Property = Microsoft.Maui.Controls.Button.PaddingProperty, Value = new Thickness(16, 10) });
			windowsButtonStyle.Setters.Add(new Setter { Property = Microsoft.Maui.Controls.Button.FontSizeProperty, Value = 14 });
			Resources.Add("AppButtonWindows", windowsButtonStyle);
		}
#endif

		// TODO(RTL): the Arabic-first FlowDirection switch (dir="rtl" in the mockups) hooks in here,
		// e.g. `MainPage.FlowDirection = FlowDirection.RightToLeft;` once the window/root view exists.

		// Apply the persisted dark-mode preference eagerly at startup. SettingsViewModel is
		// registered Transient and only constructed when the Settings page is opened, so without
		// this, UserAppTheme stays Unspecified and the app silently follows the OS theme instead
		// of the mockups' light-theme default (per projects.html, dark mode starts OFF).
		// A `--theme=light|dark` startup argument overrides the stored preference so each page can be
		// captured deterministically in both themes during design-parity verification.
		UserAppTheme = StartupNavigation.ResolveTheme(
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
		// kicks off their already-implemented `StartAsync` loops once, at process startup.
		// Only THIS launch's explicit --seed-design-data argument keeps the pipeline offline —
		// a persisted preference from a previous run can no longer do so (see fix note above).
		//
		// FIX (whole-window freeze while a fresh database was being filled): this constructor runs
		// on the UI thread, so it carries WinUI's SynchronizationContext. Calling StartAsync
		// directly meant every `await` inside `PollService.RunLoopAsync` and inside each
		// `EnrichmentWorker.RunAsync` resumed *on the UI thread* - so the ~165 KB listing parse,
		// every detail-page parse and every SQLite write for the whole backlog executed on the
		// dispatcher and the window stopped responding until the queue drained. `Task.Run` starts
		// both loops on the thread pool with no captured context; UI-bound state still reaches the
		// UI safely because `GlobalAppStatusService` marshals its own PropertyChanged.
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

		HandleDebugJsonArgument(services, Environment.GetCommandLineArgs());
	}

	private void HandleDebugJsonArgument(IServiceProvider services, string[] args)
	{
#if DEBUG
		if (!args.Contains("--debug-via-json")) return;

		Task.Run(async () =>
		{
			// Wait for 15 seconds to allow polling and enrichment to happen.
			await Task.Delay(15000);

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

			// Also dispose the published time update service to stop its timer.
			if (Handler?.MauiContext?.Services.GetService<PublishedTimeUpdateService>() is { } service)
			{
				service.Dispose();
			}
		}
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		var window = new Window(new AppShell(StartupNavigation.FromArguments(Environment.GetCommandLineArgs())));

		// The mockups are authored against a fixed 1280x800 desktop viewport, so the window opens
		// sized so its *client* area is exactly that: `Window.Height` on Windows covers the whole
		// frame, so the 32px caption band is added on top. This keeps design-parity captures
		// deterministic instead of depending on whatever size the OS last remembered.
		window.Width = 1280;
		window.Height = 800 + WindowsCaptionHeight + WindowsFrameInset;

		return window;
	}
}
