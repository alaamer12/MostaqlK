using Microsoft.Extensions.DependencyInjection;
using MostaqlK.Services.Pipeline;
using MostaqlK.Services.Pipeline.WorkerPool;

namespace MostaqlK;

public partial class App : Application
{
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
		UserAppTheme = Microsoft.Maui.Storage.Preferences.Get("settings_is_dark_mode", false)
			? AppTheme.Dark
			: AppTheme.Light;

		// MAUI has no ASP.NET-style `IHostedService`, so the pipeline subsystem (Poll Service +
		// Worker Pool) is started here as fire-and-forget background loops off the app's own
		// lifetime token. Both are registered as singletons in `MauiProgram`, so this simply
		// kicks off their already-implemented `StartAsync` loops once, at process startup.
		var pollService = services.GetRequiredService<IPollService>();
		var workerPool = services.GetRequiredService<WorkerPool>();
		_ = pollService.StartAsync(_pipelineCts.Token);
		_ = workerPool.StartAsync(_pipelineCts.Token);
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		return new Window(new AppShell(StartupNavigation.FromArguments(Environment.GetCommandLineArgs())));
	}
}