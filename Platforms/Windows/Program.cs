using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using Microsoft.Windows.AppNotifications;
using MostaqlK.Infrastructure.Notifications;
using MostaqlK.Platforms.Windows;
using MostaqlK.Services.Diagnostics;
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
    private const string SingletonMutexName = @"Local\MostaqlK.App.Singleton";
    private const string WakeEventName = @"Local\MostaqlK.App.Wake";
    private const string AppInstanceKey = "MostaqlK.App.Singleton.Instance";

    private static Mutex? _singletonMutex;
    private static EventWaitHandle? _wakeEvent;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetModuleFileName(IntPtr hModule, StringBuilder lpFilename, int nSize);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetDllDirectory(string lpPathName);

    [DllImport("Microsoft.WindowsAppRuntime.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int WindowsAppRuntime_EnsureIsLoaded();

    [STAThread]
    static void Main(string[] args)
    {
        EnsurePersistentBundleExtraction();

        LogDebug($"Main started with args: [{string.Join(", ", args)}]");
        LogDebug($"AppContext.BaseDirectory: {AppContext.BaseDirectory}");
        LogDebug($"Environment.CurrentDirectory: {Environment.CurrentDirectory}");

        CrashReporter.RegisterGlobalHandlers();
        CrashReporter.CheckAndReportPreviousCrashes();

        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            CrashReporter.Report("Program.AppDomain.UnhandledException", e.ExceptionObject as Exception, isFatal: e.IsTerminating);
        };

        TaskScheduler.UnobservedTaskException += (s, e) =>
        {
            CrashReporter.Report("Program.TaskScheduler.UnobservedTaskException", e.Exception, isFatal: false);
        };

        AppDomain.CurrentDomain.FirstChanceException += (s, e) =>
        {
            LogDebug($"[FirstChanceException] {e.Exception.GetType().FullName}: {e.Exception.Message}\n{e.Exception.StackTrace}");
        };

        // Toast COM / notification-center clicks launch LocalServer32 (this exe) even while the
        // tray process is still alive. Showing the splash before we know we are the primary
        // instance is what made a 10h-idle app look like a cold start.
        if (!TryAcquireSingletonMutex())
        {
            HandleSecondaryInstance();
            return;
        }

        StartWakeListener();

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
                try
                {
                    LogDebug("Showing NativeSplashScreen (primary instance)");
                    NativeSplashScreen.Show();
                }
                catch (Exception ex)
                {
                    LogDebug($"NativeSplashScreen.Show threw: {ex}");
                }

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
                LogDebug("App redirected to existing instance; exiting without splash.");
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
            CrashReporter.Report(source, ex, isFatal: true);
            LogDebug($"CRASH in {source}: {ex}");
        }
        catch { }
    }

    /// <summary>
    /// Configures the .NET single-file bundle extraction location to a persistent directory
    /// (<c>%LocalAppData%\MostaqlK\bundle-cache</c>) so that Windows Storage Sense or background
    /// temp file maintenance will never purge runtime DLLs and XAML assets during long-running sessions.
    /// </summary>
    private static void EnsurePersistentBundleExtraction()
    {
        try
        {
            var bundleCacheDir = MostaqlK.Core.Platform.AppPaths.BundleCacheDirectory;
            Environment.SetEnvironmentVariable("DOTNET_BUNDLE_EXTRACT_BASE_DIR", bundleCacheDir, EnvironmentVariableTarget.Process);

            var currentVal = Environment.GetEnvironmentVariable("DOTNET_BUNDLE_EXTRACT_BASE_DIR", EnvironmentVariableTarget.User);
            if (!string.Equals(currentVal, bundleCacheDir, StringComparison.OrdinalIgnoreCase))
            {
                Environment.SetEnvironmentVariable("DOTNET_BUNDLE_EXTRACT_BASE_DIR", bundleCacheDir, EnvironmentVariableTarget.User);
                BroadcastEnvironmentChange();
                LogDebug($"Configured DOTNET_BUNDLE_EXTRACT_BASE_DIR in User environment: {bundleCacheDir}");
            }
        }
        catch (Exception ex)
        {
            LogDebug($"EnsurePersistentBundleExtraction note: {ex.Message}");
        }
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
        var keyInstance = AppInstance.FindOrRegisterForKey(AppInstanceKey);

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
        TryOpenUrlFromActivation(e);
        TryRestoreExistingWindow();
    }

    private static bool TryAcquireSingletonMutex()
    {
        try
        {
            _singletonMutex = new Mutex(initiallyOwned: true, SingletonMutexName, out var createdNew);
            if (createdNew)
            {
                return true;
            }

            _singletonMutex.Dispose();
            _singletonMutex = null;
            return false;
        }
        catch (AbandonedMutexException)
        {
            // Previous owner crashed without releasing; this process is now the primary.
            return true;
        }
        catch (Exception ex)
        {
            LogDebug($"TryAcquireSingletonMutex failed (treating as primary): {ex}");
            return true;
        }
    }

    private static void StartWakeListener()
    {
        try
        {
            _wakeEvent = new EventWaitHandle(false, EventResetMode.AutoReset, WakeEventName);
            var thread = new Thread(() =>
            {
                while (_wakeEvent is not null)
                {
                    try
                    {
                        _wakeEvent.WaitOne();
                        TryRestoreExistingWindow();
                    }
                    catch (Exception ex)
                    {
                        LogDebug($"Wake listener: {ex.Message}");
                    }
                }
            })
            {
                IsBackground = true,
                Name = "MostaqlK.SingletonWake"
            };
            thread.Start();
        }
        catch (Exception ex)
        {
            LogDebug($"StartWakeListener failed: {ex.Message}");
        }
    }

    private static void HandleSecondaryInstance()
    {
        LogDebug("Secondary instance (toast/COM/activation). No splash; hand off to running process.");

        AppActivationArguments? args = null;
        try
        {
            WinRT.ComWrappersSupport.InitializeComWrappers();
            args = AppInstance.GetCurrent().GetActivatedEventArgs();
            TryOpenUrlFromActivation(args);

            foreach (var instance in AppInstance.GetInstances())
            {
                if (instance.IsCurrent)
                {
                    continue;
                }

                instance.RedirectActivationToAsync(args).AsTask().Wait(TimeSpan.FromSeconds(5));
                LogDebug("Redirected activation to an existing AppInstance.");
                break;
            }
        }
        catch (Exception ex)
        {
            LogDebug($"Secondary AppInstance handoff failed: {ex}");
            TryOpenUrlFromActivation(args);
        }

        try
        {
            if (EventWaitHandle.TryOpenExisting(WakeEventName, out var wake))
            {
                using (wake)
                {
                    wake.Set();
                }
            }
        }
        catch (Exception ex)
        {
            LogDebug($"Secondary wake pulse failed: {ex.Message}");
        }
    }

    private static void TryOpenUrlFromActivation(AppActivationArguments? args)
    {
        if (args is null)
        {
            return;
        }

        try
        {
            if (args.Data is AppNotificationActivatedEventArgs toast &&
                toast.Arguments.TryGetValue("openUrl", out var url) &&
                !string.IsNullOrWhiteSpace(url))
            {
                NotificationUrlLauncher.OpenUrl(url, "Program.Activation");
            }
        }
        catch (Exception ex)
        {
            LogDebug($"TryOpenUrlFromActivation: {ex.Message}");
        }
    }

    private static void TryRestoreExistingWindow()
    {
        try
        {
            if (MauiWinUIApplication.Current is Microsoft.Maui.MauiWinUIApplication mauiApp)
            {
                mauiApp.Services.GetService<UI.TrayIcon.TrayIconService>()?.RequestRestore();
            }
        }
        catch (Exception ex)
        {
            LogDebug($"TryRestoreExistingWindow: {ex.Message}");
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr hWnd,
        uint msg,
        UIntPtr wParam,
        string lParam,
        uint fuFlags,
        uint uTimeout,
        out UIntPtr lpdwResult);

    private static void BroadcastEnvironmentChange()
    {
        try
        {
            const uint WM_SETTINGCHANGE = 0x001A;
            const uint SMTO_ABORTIFHUNG = 0x0002;
            SendMessageTimeout(
                new IntPtr(0xFFFF),
                WM_SETTINGCHANGE,
                UIntPtr.Zero,
                "Environment",
                SMTO_ABORTIFHUNG,
                1000,
                out _);
        }
        catch
        {
            // Best-effort: Explorer/toast COM children pick up User env on next launch even without this.
        }
    }
}
