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
                // A WinRT failure that crosses into XAML's own error boundary arrives here as a bare
                // InvalidCastException (0x80004002 "No such interface supported") whose own StackTrace
                // is EMPTY - the native caller that failed left no managed frames behind. The live
                // stack of this thread still shows which of OUR calls was in flight, which is the
                // only way to name the failing API instead of guessing from a dump offset.
                var liveStack = new System.Diagnostics.StackTrace(true).ToString();
                MostaqlK.Services.Diagnostics.CrashReporter.Report(
                    "WinUI.UnhandledException",
                    e.Exception,
                    new { e.Message, LiveStack = liveStack },
                    isFatal: false);
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

