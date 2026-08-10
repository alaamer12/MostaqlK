using Microsoft.Extensions.DependencyInjection;
using MostaqlK.Infrastructure.Database;
using MostaqlK.Services.Pipeline;
using MostaqlK.Services.Pipeline.WorkerPool;

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
		if (!explicitlySeededThisLaunch)
		{
			var pollService = services.GetRequiredService<IPollService>();
			var workerPool = services.GetRequiredService<WorkerPool>();
			_ = pollService.StartAsync(_pipelineCts.Token);
			_ = workerPool.StartAsync(_pipelineCts.Token);
			MostaqlK.Services.Diagnostics.InteractionLogger.Mark("App.Startup.PipelineStarted", "A");
		}
		else
		{
			MostaqlK.Services.Diagnostics.InteractionLogger.Mark("App.Startup.PipelineSkipped", "B");
		}
	}

	/// <summary>
	/// Handles the <c>--seed-design-data[=off]</c> startup argument and returns whether the app is
	/// currently running against seeded design-parity data (this launch's request if provided,
	/// otherwise the persisted preference — for display/logging purposes only; see the
	/// <c>explicitlySeededThisLaunch</c> check in the constructor for the actual pipeline gate).
	/// </summary>
	private static bool ApplyDesignDataArgument(IServiceProvider services, string[] args)
	{
		var requested = DesignDataSeeder.ParseArguments(args);
		if (requested is null)
		{
			return Microsoft.Maui.Storage.Preferences.Get(DesignDataSeeder.PreferenceKey, false);
		}

		if (requested.Value)
		{
			services.GetRequiredService<DesignDataSeeder>().SeedAsync().GetAwaiter().GetResult();
		}

		Microsoft.Maui.Storage.Preferences.Set(DesignDataSeeder.PreferenceKey, requested.Value);
		return requested.Value;
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