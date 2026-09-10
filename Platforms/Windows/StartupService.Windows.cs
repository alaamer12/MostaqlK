using Microsoft.Win32;
using MostaqlK.Services;
using System.Diagnostics;

namespace MostaqlK.Platforms.Windows;

/// <summary>
/// Windows implementation of <see cref="IStartupService"/>. Reads and writes
/// <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c> to register or unregister
/// MostaqlK as a per-user startup application.
/// <para>
/// The registry value stores the full quoted path to the current executable followed by
/// <c>--silent-start</c>, so the app starts directly in the tray (no splash screen, no
/// main window) when Windows invokes it at login. The flag is consumed by
/// <c>Platforms/Windows/Program.cs</c> and <c>PlatformServiceRegistration.cs</c>.
/// </para>
/// <para>
/// Uses <c>HKCU</c> — no elevation required; this is the standard per-user approach for
/// tray-resident desktop utilities.
/// </para>
/// </summary>
internal sealed class WindowsStartupService : IStartupService
{
    private const string RunKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "MostaqlK";

    /// <inheritdoc />
    public bool IsSupported => true;

    /// <inheritdoc />
    public bool IsStartupEnabled
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
                return key?.GetValue(AppName) is not null;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <inheritdoc />
    public bool SetStartup(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            if (key is null)
            {
                return false;
            }

            if (enable)
            {
                // Always quote the path: installer paths frequently contain spaces.
                var exePath = Process.GetCurrentProcess().MainModule?.FileName;
                if (string.IsNullOrEmpty(exePath))
                {
                    return false;
                }

                // --silent-start tells the process to skip the splash screen and start
                // hidden in the tray (see Program.cs and PlatformServiceRegistration.cs).
                key.SetValue(AppName, $"\"{exePath}\" --silent-start");
            }
            else
            {
                // throwOnMissingValue: false — idempotent delete is safe.
                key.DeleteValue(AppName, throwOnMissingValue: false);
            }

            return true;
        }
        catch
        {
            // Registry access may fail in sandbox/restricted environments; surface as false
            // so the ViewModel can revert the UI toggle instead of silently lying.
            return false;
        }
    }

    /// <inheritdoc />
    public bool SetStartupEnabled(bool enable) => SetStartup(enable);
}
