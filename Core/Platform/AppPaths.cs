using System;
using System.IO;

namespace MostaqlK.Core.Platform;

/// <summary>
/// Cross-platform directory provider for MostaqlK following platform-native filesystem conventions
/// across Windows, macOS, Linux, Android, and iOS.
/// </summary>
public interface IAppDirectoryProvider
{
    /// <summary>
    /// Root application directory.
    /// <list type="bullet">
    /// <item>Windows: <c>%LocalAppData%\MostaqlK</c></item>
    /// <item>macOS: <c>~/Library/Application Support/MostaqlK</c></item>
    /// <item>Linux: <c>~/.local/share/mostaqlk</c> (or <c>$XDG_DATA_HOME/mostaqlk</c>)</item>
    /// <item>Android: <c>Context.FilesDir</c> (internal app storage)</item>
    /// <item>iOS: <c>Library/Application Support</c></item>
    /// </list>
    /// </summary>
    string AppDirectory { get; }

    /// <summary>
    /// Persistent data directory for application databases and durable state.
    /// </summary>
    string DataDirectory { get; }

    /// <summary>
    /// Persistent settings directory (<c>Settings/</c>) for preferences and configuration files.
    /// </summary>
    string SettingsDirectory { get; }

    /// <summary>
    /// Path to the preferences file (<c>preferences.dat</c>).
    /// </summary>
    string PreferencesFilePath { get; }

    /// <summary>
    /// Diagnostics log directory (<c>log/</c>) inside the application directory.
    /// </summary>
    string LogsDirectory { get; }

    /// <summary>
    /// Cache directory for downloaded temporary assets and scratch files.
    /// </summary>
    string CacheDirectory { get; }

    /// <summary>
    /// Path to the SQLite database file (<c>mostaqlk.db</c>).
    /// </summary>
    string DatabasePath { get; }

    /// <summary>
    /// Path to the primary interaction log file (<c>interaction-log.txt</c>).
    /// </summary>
    string LogFilePath { get; }

    /// <summary>
    /// Path to the dedicated crash log file (<c>crash.log</c>).
    /// </summary>
    string CrashLogFilePath { get; }

    /// <summary>
    /// Path to the attachments cache directory.
    /// </summary>
    string AttachmentsDirectory { get; }

    /// <summary>
    /// Ensures standard directories exist and purges legacy unidiomatic paths if they exist.
    /// </summary>
    void EnsureDirectories();
}

/// <summary>
/// Canonical provider and static access point for all platform filesystem paths in MostaqlK.
/// </summary>
public static class AppPaths
{
    private static readonly Lazy<string> BaseAppDir = new(ResolveAppDirectory);
    private static readonly Lazy<string> BaseDataDir = new(ResolveDataDirectory);
    private static readonly Lazy<string> BaseSettingsDir = new(ResolveSettingsDirectory);
    private static readonly Lazy<string> BaseLogsDir = new(ResolveLogsDirectory);
    private static readonly Lazy<string> BaseCacheDir = new(ResolveCacheDirectory);

    private static readonly object InitLock = new();
    private static bool _initialized;

    public static string AppName => "MostaqlK";
    public static string PackageName => "com.mostaqlk";

    /// <summary>
    /// Root application directory for MostaqlK.
    /// </summary>
    public static string AppDirectory
    {
        get
        {
            var dir = BaseAppDir.Value;
            EnsureDirectoryExists(dir);
            return dir;
        }
    }

    /// <summary>
    /// Persistent application data directory (<c>Data/</c>).
    /// </summary>
    public static string DataDirectory
    {
        get
        {
            var dir = BaseDataDir.Value;
            EnsureDirectoryExists(dir);
            return dir;
        }
    }

    /// <summary>
    /// Persistent application settings directory (<c>Settings/</c>).
    /// </summary>
    public static string SettingsDirectory
    {
        get
        {
            var dir = BaseSettingsDir.Value;
            EnsureDirectoryExists(dir);
            return dir;
        }
    }

    /// <summary>
    /// Primary preferences file path (<c>preferences.dat</c>).
    /// </summary>
    public static string PreferencesFilePath => Path.Combine(SettingsDirectory, "preferences.dat");

    /// <summary>
    /// Application logs directory (<c>log/</c>) inside the application directory.
    /// </summary>
    public static string LogsDirectory
    {
        get
        {
            var dir = BaseLogsDir.Value;
            EnsureDirectoryExists(dir);
            return dir;
        }
    }

    /// <summary>
    /// Cache and temporary file directory.
    /// </summary>
    public static string CacheDirectory
    {
        get
        {
            var dir = BaseCacheDir.Value;
            EnsureDirectoryExists(dir);
            return dir;
        }
    }

    /// <summary>
    /// Primary SQLite database path (<c>mostaqlk.db</c>).
    /// </summary>
    public static string DatabasePath => Path.Combine(DataDirectory, "mostaqlk.db");

    /// <summary>
    /// Primary interaction log file path (<c>interaction-log.txt</c>).
    /// </summary>
    public static string LogFilePath => Path.Combine(LogsDirectory, "interaction-log.txt");

    /// <summary>
    /// Dedicated crash log file path (<c>crash.log</c>).
    /// </summary>
    public static string CrashLogFilePath => Path.Combine(LogsDirectory, "crash.log");

    /// <summary>
    /// Directory for downloaded attachment files.
    /// </summary>
    public static string AttachmentsDirectory => Path.Combine(CacheDirectory, "attachments");

    /// <summary>
    /// Ensures standard directories exist and cleans up any old buggy legacy folders.
    /// </summary>
    public static void Initialize()
    {
        if (_initialized) return;

        lock (InitLock)
        {
            if (_initialized) return;

            try
            {
                CleanupLegacyDirectories();

                EnsureDirectoryExists(AppDirectory);
                EnsureDirectoryExists(DataDirectory);
                EnsureDirectoryExists(SettingsDirectory);
                EnsureDirectoryExists(LogsDirectory);
                EnsureDirectoryExists(CacheDirectory);
                EnsureDirectoryExists(AttachmentsDirectory);
            }
            catch
            {
                // Never crash app startup due to directory initialization.
            }
            finally
            {
                _initialized = true;
            }
        }
    }

    private static void EnsureDirectoryExists(string dir)
    {
        if (!Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
    }

    private static string ResolveAppDirectory()
    {
#if ANDROID || IOS
        try
        {
            if (!string.IsNullOrEmpty(Microsoft.Maui.Storage.FileSystem.AppDataDirectory))
            {
                return Microsoft.Maui.Storage.FileSystem.AppDataDirectory;
            }
        }
        catch { }
#endif

        if (OperatingSystem.IsWindows())
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(localAppData))
            {
                localAppData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "Local");
            }
            return Path.Combine(localAppData, AppName);
        }

        if (OperatingSystem.IsMacOS() || OperatingSystem.IsMacCatalyst())
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, "Library", "Application Support", AppName);
        }

        if (OperatingSystem.IsLinux())
        {
            var xdgData = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
            if (string.IsNullOrWhiteSpace(xdgData))
            {
                var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                xdgData = Path.Combine(home, ".local", "share");
            }
            return Path.Combine(xdgData, "mostaqlk");
        }

#if ANDROID || IOS || MACCATALYST
        try
        {
            return Microsoft.Maui.Storage.FileSystem.AppDataDirectory;
        }
        catch { }
#endif
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppName);
    }

    private static string ResolveDataDirectory()
    {
#if ANDROID || IOS
        return AppDirectory;
#else
        return Path.Combine(AppDirectory, "Data");
#endif
    }

    private static string ResolveSettingsDirectory()
    {
#if ANDROID || IOS
        return AppDirectory;
#else
        return Path.Combine(AppDirectory, "Settings");
#endif
    }

    private static string ResolveLogsDirectory()
    {
        return Path.Combine(AppDirectory, "log");
    }

    private static string ResolveCacheDirectory()
    {
#if ANDROID || IOS
        try
        {
            if (!string.IsNullOrEmpty(Microsoft.Maui.Storage.FileSystem.CacheDirectory))
            {
                return Microsoft.Maui.Storage.FileSystem.CacheDirectory;
            }
        }
        catch { }
#endif

        if (OperatingSystem.IsWindows())
        {
            return Path.Combine(AppDirectory, "Cache");
        }

        if (OperatingSystem.IsMacOS() || OperatingSystem.IsMacCatalyst())
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, "Library", "Caches", AppName);
        }

        if (OperatingSystem.IsLinux())
        {
            var xdgCache = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
            if (string.IsNullOrWhiteSpace(xdgCache))
            {
                var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                xdgCache = Path.Combine(home, ".cache");
            }
            return Path.Combine(xdgCache, "mostaqlk");
        }

#if ANDROID || IOS || MACCATALYST
        try
        {
            return Microsoft.Maui.Storage.FileSystem.CacheDirectory;
        }
        catch { }
#endif
        return Path.Combine(AppDirectory, "Cache");
    }

    /// <summary>
    /// Removes legacy unidiomatic / bug-generated folders like `%LocalAppData%\User Name`
    /// or `%LocalAppData%\com.companyname.mostaqlk`.
    /// </summary>
    public static void CleanupLegacyDirectories()
    {
        if (!OperatingSystem.IsWindows()) return;

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData)) return;

        string[] legacyFoldersToDelete =
        [
            Path.Combine(localAppData, "User Name"),
            Path.Combine(localAppData, "com.companyname.mostaqlk"),
            Path.Combine(localAppData, "com.mostaqlk")
        ];

        foreach (var folder in legacyFoldersToDelete)
        {
            try
            {
                if (Directory.Exists(folder))
                {
                    Directory.Delete(folder, recursive: true);
                }
            }
            catch
            {
                // Best-effort cleanup
            }
        }
    }
}
