using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using MostaqlK.Platforms.Windows;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace MostaqlK.WinUI;

/// <summary>
/// Custom entry point for the Windows application to handle single-instance (singleton) behavior.
/// It uses the Windows App SDK AppLifecycle API to find or register a unique key for the app.
/// </summary>
public static class Program
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetModuleFileName(IntPtr hModule, StringBuilder lpFilename, int nSize);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetDllDirectory(string lpPathName);

    [DllImport("Microsoft.WindowsAppRuntime.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int WindowsAppRuntime_EnsureIsLoaded();

    [STAThread]
    static void Main(string[] args)
    {
        LogDebug($"Main started with args: [{string.Join(", ", args)}]");
        LogDebug($"AppContext.BaseDirectory: {AppContext.BaseDirectory}");
        LogDebug($"Environment.CurrentDirectory: {Environment.CurrentDirectory}");

        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            LogCrash("AppDomain.UnhandledException", e.ExceptionObject as Exception);
        };

        TaskScheduler.UnobservedTaskException += (s, e) =>
        {
            LogCrash("TaskScheduler.UnobservedTaskException", e.Exception);
        };

        AppDomain.CurrentDomain.FirstChanceException += (s, e) =>
        {
            LogDebug($"[FirstChanceException] {e.Exception.GetType().FullName}: {e.Exception.Message}\n{e.Exception.StackTrace}");
        };

        // Show native splash screen immediately on launch before any heavy runtime initialization
        try
        {
            LogDebug("Showing NativeSplashScreen");
            NativeSplashScreen.Show();
        }
        catch (Exception ex)
        {
            LogDebug($"NativeSplashScreen.Show threw: {ex}");
        }

        try
        {
            LogDebug("Calling InitializeWindowsAppRuntime()");
            InitializeWindowsAppRuntime();

            LogDebug("Calling WinRT.ComWrappersSupport.InitializeComWrappers()");
            WinRT.ComWrappersSupport.InitializeComWrappers();

            bool isRedirect = false;
            try
            {
                LogDebug("Calling DecideRedirection()");
                isRedirect = DecideRedirection();
                LogDebug($"DecideRedirection returned: {isRedirect}");
            }
            catch (Exception ex)
            {
                LogDebug($"DecideRedirection failed (continuing as primary instance): {ex}");
                isRedirect = false;
            }

            if (!isRedirect)
            {
                LogDebug("Calling Microsoft.UI.Xaml.Application.Start");
                Microsoft.UI.Xaml.Application.Start((p) =>
                {
                    LogDebug("Inside Application.Start callback");
                    var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
                    SynchronizationContext.SetSynchronizationContext(context);
                    LogDebug("Creating new App()");
                    new App();
                    LogDebug("App() created successfully");
                });
            }
            else
            {
                LogDebug("App redirected to existing instance. Hiding splash.");
                NativeSplashScreen.Hide();
            }
        }
        catch (Exception ex)
        {
            LogCrash("Main.Exception", ex);
            NativeSplashScreen.Hide();
            throw;
        }
    }

    private static void LogDebug(string msg)
    {
        try
        {
            string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {msg}\n";
            string logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MostaqlK", "log");
            Directory.CreateDirectory(logDir);
            File.AppendAllText(Path.Combine(logDir, "startup-debug.log"), line);
        }
        catch { }
    }

    private static void LogCrash(string source, Exception? ex)
    {
        try
        {
            string msg = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{source}] {ex}\n";
            LogDebug($"CRASH in {source}: {ex}");
            string logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MostaqlK", "log");
            Directory.CreateDirectory(logDir);
            File.AppendAllText(Path.Combine(logDir, "crash.log"), msg);
        }
        catch { }
    }

    /// <summary>
    /// Configures the base directory for Windows App SDK Undocked RegFree WinRT and DLL search path.
    /// In .NET single-file publish, native DLLs are extracted to the temporary bundle directory
    /// rather than AppContext.BaseDirectory (which is the directory containing the .exe).
    /// </summary>
    private static void InitializeWindowsAppRuntime()
    {
        try
        {
            LogDebug("Starting InitializeWindowsAppRuntime");
            bool loaded = NativeLibrary.TryLoad("Microsoft.WindowsAppRuntime.dll", typeof(Program).Assembly, null, out IntPtr handle);
            LogDebug($"NativeLibrary.TryLoad('Microsoft.WindowsAppRuntime.dll') returned: {loaded}, handle: {handle}");
            if (loaded)
            {
                var sb = new StringBuilder(1024);
                if (GetModuleFileName(handle, sb, sb.Capacity) > 0)
                {
                    string dllPath = sb.ToString();
                    LogDebug($"Module file path: {dllPath}");
                    string? dir = Path.GetDirectoryName(dllPath);
                    if (!string.IsNullOrEmpty(dir))
                    {
                        if (!dir.EndsWith('\\'))
                        {
                            dir += "\\";
                        }
                        LogDebug($"Setting SetDllDirectory={dir}");
                        SetDllDirectory(dir);

                        string currentPath = Environment.GetEnvironmentVariable("PATH") ?? "";
                        if (!currentPath.Contains(dir, StringComparison.OrdinalIgnoreCase))
                        {
                            Environment.SetEnvironmentVariable("PATH", dir + ";" + currentPath);
                        }

                        LogDebug($"Setting MICROSOFT_WINDOWSAPPRUNTIME_BASE_DIRECTORY={dir}");
                        Environment.SetEnvironmentVariable("MICROSOFT_WINDOWSAPPRUNTIME_BASE_DIRECTORY", dir);
                        try
                        {
                            int res = WindowsAppRuntime_EnsureIsLoaded();
                            LogDebug($"WindowsAppRuntime_EnsureIsLoaded returned {res}");
                        }
                        catch (Exception ex)
                        {
                            LogDebug($"WindowsAppRuntime_EnsureIsLoaded threw: {ex}");
                        }
                        return;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            LogDebug($"InitializeWindowsAppRuntime catch: {ex}");
        }

        string baseDir = AppContext.BaseDirectory;
        if (!baseDir.EndsWith('\\'))
        {
            baseDir += "\\";
        }
        SetDllDirectory(baseDir);
        LogDebug($"Fallback setting MICROSOFT_WINDOWSAPPRUNTIME_BASE_DIRECTORY={baseDir}");
        Environment.SetEnvironmentVariable("MICROSOFT_WINDOWSAPPRUNTIME_BASE_DIRECTORY", baseDir);
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
