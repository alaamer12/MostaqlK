using Microsoft.Extensions.DependencyInjection;

namespace MostaqlK;

public partial class App : Application
{
	public App()
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
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		return new Window(new AppShell());
	}
}