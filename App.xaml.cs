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
		// Windows target framework, per Mechanism 1 in cross-platform-ui-conventions.md.
#if WINDOWS
		Resources.MergedDictionaries.Add(new ResourceDictionary
		{
			Source = new Uri("Platforms/Windows/Styles/AppButtonStyle.Windows.xaml", UriKind.Relative)
		});
#endif

		// TODO(RTL): the Arabic-first FlowDirection switch (dir="rtl" in the mockups) hooks in here,
		// e.g. `MainPage.FlowDirection = FlowDirection.RightToLeft;` once the window/root view exists.

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
		return new Window(new AppShell());
	}
}