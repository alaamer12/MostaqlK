using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using System;
using System.Threading;

namespace MostaqlK.WinUI;

/// <summary>
/// Custom entry point for the Windows application to handle single-instance (singleton) behavior.
/// It uses the Windows App SDK AppLifecycle API to find or register a unique key for the app.
/// </summary>
public static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();

        bool isRedirect = DecideRedirection();
        if (!isRedirect)
        {
            Microsoft.UI.Xaml.Application.Start((p) =>
            {
                var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
                SynchronizationContext.SetSynchronizationContext(context);
                new App();
            });
        }
    }

    /// <summary>
    /// Checks if another instance of the app is already running.
    /// If so, redirects the current activation to that instance and returns true.
    /// </summary>
    private static bool DecideRedirection()
    {
        bool isRedirect = false;
        
        // Get the arguments with which the current instance was activated.
        var args = AppInstance.GetCurrent().GetActivatedEventArgs();

        // Find or register a unique key for our application.
        // If the key is already registered, it returns the instance that registered it.
        var keyInstance = AppInstance.FindOrRegisterForKey("MostaqlK.App.Singleton.Instance");

        if (keyInstance.IsCurrent)
        {
            // This is the first instance (or we successfully registered the key).
            // Hook up the Activated event to handle subsequent launch attempts.
            keyInstance.Activated += OnActivated;
        }
        else
        {
            isRedirect = true;
            
            // Redirect the activation to the existing instance and wait for it to complete.
            // Using .AsTask().Wait() because we are in a synchronous Main method.
            keyInstance.RedirectActivationToAsync(args).AsTask().Wait();
        }

        return isRedirect;
    }

    /// <summary>
    /// Called when a subsequent instance is launched and redirected to this instance.
    /// </summary>
    private static void OnActivated(object? sender, AppActivationArguments e)
    {
        // When redirected activation occurs, we want to bring the existing window to the foreground.
        // Since we are running in the context of the MauiWinUIApplication, we can use the 
        // TrayIconService or App lifecycle hooks to restore the window.
        
        if (MauiWinUIApplication.Current is Microsoft.Maui.MauiWinUIApplication mauiApp)
        {
            var trayIconService = mauiApp.Services.GetService<UI.TrayIcon.TrayIconService>();
            trayIconService?.RequestRestore();
        }
    }
}
