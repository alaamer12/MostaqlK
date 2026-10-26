using Microsoft.UI.Xaml;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace MostaqlK.WinUI;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : MauiWinUIApplication
{
    /// <summary>
    /// Initializes the singleton application object.  This is the first line of authored code
    /// executed, and as such is the logical equivalent of main() or WinMain().
    /// </summary>
    public App()
    {
        this.InitializeComponent();

        // Handle WinUI 3 unhandled XAML/COM exceptions to prevent fatal process termination
        // (e.g. D3D device-lost / compositor / stale visual tree release errors during background hibernation).
        this.UnhandledException += (sender, e) =>
        {
            try
            {
                MostaqlK.Services.Diagnostics.CrashReporter.Report("WinUI.UnhandledException", e.Exception, new { e.Message }, isFatal: false);
                e.Handled = true;
            }
            catch
            {
                // Fail-safe to avoid throwing inside the unhandled exception handler
            }
        };
    }

    protected override MauiApp CreateMauiApp() => MostaqlK.MauiProgram.CreateMauiApp();
}

