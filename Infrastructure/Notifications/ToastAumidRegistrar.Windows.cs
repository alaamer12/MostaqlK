using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using MostaqlK.Services.Diagnostics;

namespace MostaqlK.Infrastructure.Notifications;

/// <summary>
/// Registers the process-wide AppUserModelID (AUMID) and a matching Start Menu shortcut that
/// carries the same AUMID, which is what actually lets Windows display a toast for an unpackaged
/// (<c>WindowsPackageType=None</c>) app. <see cref="Microsoft.Windows.AppNotifications.AppNotificationManager.Register"/>
/// alone only registers the COM activation server — it does not give the process an identity, and
/// without one Windows silently drops the toast instead of showing it or throwing. This is the
/// well-documented "unpackaged Win32/WinUI toast" quirk (see Microsoft's "Send a local toast
/// notification from unpackaged apps" guidance and the classic
/// <c>DesktopNotificationManagerCompat</c> shortcut+AUMID pattern it is based on).
/// </summary>
public static class ToastAumidRegistrar
{
    /// <summary>Stable AUMID for this app; must match the shortcut's AppUserModel.ID property.</summary>
    public const string Aumid = "MostaqlK.App";

    private static readonly object Gate = new();
    private static bool _done;

    /// <summary>
    /// Idempotently: (1) sets the current process's explicit AUMID, and (2) ensures a Start Menu
    /// shortcut exists for this executable with the same AUMID stamped on it via its property
    /// store. Must run before <c>AppNotificationManager.Default.Register()</c>. Best-effort —
    /// failures are logged via <see cref="InteractionLogger"/> but never thrown, so a toast
    /// delivery attempt can still proceed (and fail informatively) rather than crash the caller.
    /// </summary>
    public static void EnsureRegistered()
    {
        if (_done)
        {
            return;
        }

        lock (Gate)
        {
            if (_done)
            {
                return;
            }

            try
            {
                var hr = SetCurrentProcessExplicitAppUserModelID(Aumid);
                if (hr != 0)
                {
                    InteractionLogger.Mark("ToastAumidRegistrar.SetAumid", "B", new { hr });
                }
                else
                {
                    InteractionLogger.Mark("ToastAumidRegistrar.SetAumid", "A", new { Aumid });
                }

                EnsureShortcut();
                EnsureActivatorRegistryEntry();
            }
            catch (Exception ex)
            {
                InteractionLogger.Fault("ToastAumidRegistrar.EnsureRegistered", ex);
            }
            finally
            {
                _done = true;
            }
        }
    }

    /// <summary>
    /// Writes <c>HKCU\Software\Classes\CLSID\{ToastActivator.ClsidString}\LocalServer32</c> so Windows
    /// knows which exe to launch/talk to for <see cref="ToastActivator"/>'s COM activation when the
    /// toast body is clicked. Best-effort/idempotent — re-writing the same value is harmless.
    /// </summary>
    private static void EnsureActivatorRegistryEntry()
    {
        var exePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
        if (string.IsNullOrWhiteSpace(exePath))
        {
            return;
        }

        using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(
            $@"Software\Classes\CLSID\{{{ToastActivator.ClsidString}}}\LocalServer32");
        key.SetValue(null, $"\"{exePath}\"");
        InteractionLogger.Mark("ToastAumidRegistrar.EnsureActivatorRegistryEntry", "A", new { exePath });
    }

    private static void EnsureShortcut()
    {
        var shortcutPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Microsoft", "Windows", "Start Menu", "Programs", "MostaqlK.lnk");

        var exePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
        if (string.IsNullOrWhiteSpace(exePath))
        {
            InteractionLogger.Mark("ToastAumidRegistrar.EnsureShortcut", "B", "no-exe-path");
            return;
        }

        // FIX ("still no notification after installing the Singleton package"): this used to
        // skip recreating the shortcut whenever the AUMID property already matched, WITHOUT ever
        // checking whether the shortcut's *target exe path* was still correct. Across dev
        // iterations (Debug/Release rebuilds, publish to a different output folder, moving the
        // install directory, etc.) it is entirely possible for a stale shortcut - created by an
        // earlier run of this exact app from a different path - to keep the right AUMID but point
        // at an exe that no longer exists or isn't the one actually running. Windows' Action
        // Center resolves the app identity (icon, display name, per-app notification toggle in
        // Settings > Notifications) from that Start Menu shortcut, so a stale target can leave the
        // app effectively "unregistered" from the user's point of view even though
        // AppNotificationManager itself reports success. Now the target path is also verified and
        // the shortcut is rewritten whenever it drifts.
        if (File.Exists(shortcutPath) && ShortcutMatches(shortcutPath, exePath))
        {
            InteractionLogger.Mark("ToastAumidRegistrar.EnsureShortcut", "A", "already-present");
            return;
        }

        // Note: any pre-existing shortcut created before ToastActivatorCLSID support was added
        // will fail ShortcutMatches below (missing/mismatched CLSID) and get rewritten here, which
        // is required for toast body clicks to start working retroactively for existing installs.

        Directory.CreateDirectory(Path.GetDirectoryName(shortcutPath)!);
        CreateShortcutWithAumid(shortcutPath, exePath, Aumid);
        InteractionLogger.Mark("ToastAumidRegistrar.EnsureShortcut", "A", new { shortcutPath, exePath, Recreated = File.Exists(shortcutPath) });
    }

    private static bool ShortcutMatches(string shortcutPath, string expectedExePath)
    {
        try
        {
            var link = (IShellLinkW)new CShellLink();
            ((IPersistFile)link).Load(shortcutPath, 0);
            var store = (IPropertyStore)link;
            store.GetValue(ref PKEY_AppUserModelID, out var value);
            var existingAumid = value.pwszVal != IntPtr.Zero ? Marshal.PtrToStringUni(value.pwszVal) : null;
            if (!string.Equals(existingAumid, Aumid, StringComparison.Ordinal))
            {
                return false;
            }

            store.GetValue(ref PKEY_ToastActivatorCLSID, out var clsidValue);
            var existingClsid = clsidValue.pwszVal != IntPtr.Zero
                ? Marshal.PtrToStructure<Guid>(clsidValue.pwszVal)
                : (Guid?)null;
            if (existingClsid != new Guid(ToastActivator.ClsidString))
            {
                return false;
            }

            var targetBuilder = new StringBuilder(260);
            link.GetPath(targetBuilder, targetBuilder.Capacity, IntPtr.Zero, 0);
            var existingTarget = targetBuilder.ToString();
            return string.Equals(existingTarget, expectedExePath, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static void CreateShortcutWithAumid(string shortcutPath, string targetExe, string aumid)
    {
        var link = (IShellLinkW)new CShellLink();
        link.SetPath(targetExe);
        link.SetWorkingDirectory(Path.GetDirectoryName(targetExe) ?? string.Empty);

        var store = (IPropertyStore)link;
        var propVariant = new PropVariant { vt = VT_LPWSTR, pwszVal = Marshal.StringToCoTaskMemUni(aumid) };
        store.SetValue(ref PKEY_AppUserModelID, ref propVariant);

        var clsidPtr = Marshal.AllocCoTaskMem(16);
        Marshal.StructureToPtr(new Guid(ToastActivator.ClsidString), clsidPtr, false);
        var clsidVariant = new PropVariant { vt = VT_CLSID, pwszVal = clsidPtr };
        store.SetValue(ref PKEY_ToastActivatorCLSID, ref clsidVariant);

        store.Commit();
        Marshal.FreeCoTaskMem(propVariant.pwszVal);
        Marshal.FreeCoTaskMem(clsidPtr);

        ((IPersistFile)link).Save(shortcutPath, true);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);

    private const short VT_LPWSTR = 31;
    private const short VT_CLSID = 72;

    private static PropertyKey PKEY_AppUserModelID = new(
        new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 5);

    private static PropertyKey PKEY_ToastActivatorCLSID = new(
        new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 26);

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey
    {
        public Guid fmtid;
        public int pid;

        public PropertyKey(Guid fmtid, int pid)
        {
            this.fmtid = fmtid;
            this.pid = pid;
        }
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct PropVariant
    {
        [FieldOffset(0)] public short vt;
        [FieldOffset(8)] public IntPtr pwszVal;
    }

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    private class CShellLink
    {
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath(StringBuilder pszFile, int cchMaxPath, IntPtr pfd, uint fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription(StringBuilder pszName, int cchMaxName);
        void SetDescription(string pszName);
        void GetWorkingDirectory(StringBuilder pszDir, int cchMaxPath);
        void SetWorkingDirectory(string pszDir);
        void SetArguments(string pszArgs);
        void GetArguments(StringBuilder pszArgs, int cchMaxPath);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation(StringBuilder pszIconPath, int cchIconPath, out int piIcon);
        void SetIconLocation(string pszIconPath, int iIcon);
        void SetRelativePath(string pszPathRel, uint dwReserved);
        void Resolve(IntPtr hwnd, uint fFlags);
        void SetPath(string pszFile);
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99")]
    private interface IPropertyStore
    {
        void GetCount(out uint cProps);
        void GetAt(uint iProp, out PropertyKey pkey);
        void GetValue(ref PropertyKey key, out PropVariant pv);
        void SetValue(ref PropertyKey key, ref PropVariant pv);
        void Commit();
    }
}
