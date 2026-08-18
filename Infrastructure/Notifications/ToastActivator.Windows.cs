using System.Runtime.InteropServices;
using MostaqlK.Services.Diagnostics;

namespace MostaqlK.Infrastructure.Notifications;

/// <summary>
/// COM activation plumbing that makes clicking the *body* of a classic WinRT
/// (<see cref="Windows.UI.Notifications.ToastNotification"/>) toast actually do something for an
/// unpackaged (<c>WindowsPackageType=None</c>) app.
/// <para>
/// Unlike the modern Windows App SDK (<c>AppNotificationManager.NotificationInvoked</c>), classic
/// WinRT toasts for Win32 apps have NO built-in "tell me when the user clicked" event — Windows
/// instead expects a COM server implementing <see cref="INotificationActivationCallback"/>, whose
/// CLSID is registered in <c>HKCU\Software\Classes\CLSID\{clsid}\LocalServer32</c> and referenced
/// from the Start Menu shortcut's <c>System.AppUserModel.ToastActivatorCLSID</c> property (see
/// <see cref="ToastAumidRegistrar"/>). Without this, clicking the toast body silently does nothing
/// — which was the root cause of "buttons ARE NOT WORKING" for the general click (the explicit
/// "عرض على مستقل" button uses protocol activation and is launched by the OS directly, so it did
/// not depend on this).
/// </para>
/// </summary>
public static class ToastActivator
{
    /// <summary>Fixed CLSID for this app's toast activation COM server. Must match the value
    /// written to the registry and stamped on the Start Menu shortcut by <see cref="ToastAumidRegistrar"/>.</summary>
    public const string ClsidString = "6A6D9142-4F91-4C6A-9C7B-6B6A6E5D9A11";

    private static readonly Guid Clsid = new(ClsidString);
    private static readonly object Gate = new();
    private static bool _registered;
    private static uint _cookie;
    private static ClassFactory? _factory;

    /// <summary>
    /// Registers the process as the COM local server for <see cref="Clsid"/>, so that clicking a
    /// toast's body (foreground activation) routes into <see cref="OnActivated"/> instead of doing
    /// nothing. Idempotent and best-effort — failures are logged but never thrown.
    /// </summary>
    public static void Register()
    {
        if (_registered) return;

        lock (Gate)
        {
            if (_registered) return;

            try
            {
                _factory = new ClassFactory(() => new ActivatorComObject());
                var clsid = Clsid;
                var hr = CoRegisterClassObject(ref clsid, _factory, CLSCTX_LOCAL_SERVER, REGCLS_MULTIPLEUSE, out _cookie);
                InteractionLogger.Mark("ToastActivator.Register", hr == 0 ? "A" : "B", new { hr });
            }
            catch (Exception ex)
            {
                InteractionLogger.Fault("ToastActivator.Register", ex);
            }
            finally
            {
                _registered = true;
            }
        }
    }

    /// <summary>
    /// Handles a toast body click (invoked args come from the toast's <c>launch</c> attribute, e.g.
    /// <c>"projectId=123"</c> or <c>"filter=unread"</c>): restores the window from the tray if
    /// hidden and navigates to the relevant page, mirroring <c>WinAppSdkVariation.OnNotificationInvoked</c>.
    /// </summary>
    private static void OnActivated(string invokedArgs)
    {
        InteractionLogger.Mark("ToastActivator.OnActivated", "A", new { Arguments = invokedArgs });

        var args = ParseArguments(invokedArgs);

        if (args.TryGetValue("openUrl", out var url) && !string.IsNullOrWhiteSpace(url))
        {
            NotificationUrlLauncher.OpenUrl(url, "ToastActivator.OnActivated");
            return;
        }

        Microsoft.Maui.ApplicationModel.MainThread.BeginInvokeOnMainThread(async () =>
        {
            try
            {
                var services = Microsoft.Maui.IPlatformApplication.Current?.Services;

                var appLifecycleService = services?.GetService<Services.AppLifecycleService>();
                if (appLifecycleService is { IsInBackground: true })
                {
                    var trayService = services?.GetService<UI.TrayIcon.TrayIconService>();
                    trayService?.OnOpen();
                }

                if (args.TryGetValue("projectId", out var projectIdStr) && long.TryParse(projectIdStr, out var projectId))
                {
                    await Shell.Current.GoToAsync($"ProjectDetailsPage?projectId={projectId}");
                }
                else
                {
                    await Shell.Current.GoToAsync("//MainWindowPage");
                }
            }
            catch (Exception ex)
            {
                InteractionLogger.Fault("ToastActivator.NavigateFromActivation", ex, new { Arguments = invokedArgs });
            }
        });
    }

    private static Dictionary<string, string> ParseArguments(string invokedArgs)
    {
        var result = new Dictionary<string, string>();
        if (string.IsNullOrWhiteSpace(invokedArgs)) return result;

        foreach (var pair in invokedArgs.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var idx = pair.IndexOf('=');
            if (idx <= 0) continue;
            // Values (e.g. 'openUrl') are Uri.EscapeDataString-encoded by the sender precisely so
            // their own '&'/'=' characters survive this naive '&'-separated split intact.
            result[pair[..idx]] = Uri.UnescapeDataString(pair[(idx + 1)..]);
        }

        return result;
    }

    private const uint CLSCTX_LOCAL_SERVER = 0x4;
    private const uint REGCLS_MULTIPLEUSE = 1;

    [DllImport("ole32.dll")]
    private static extern int CoRegisterClassObject(
        ref Guid rclsid,
        [MarshalAs(UnmanagedType.IUnknown)] object pUnk,
        uint dwClsContext,
        uint flags,
        out uint lpdwRegister);

    [ComImport, Guid("53E31837-6600-4A81-9395-75CFFE746F94"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface INotificationActivationCallback
    {
        void Activate(
            [MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
            [MarshalAs(UnmanagedType.LPWStr)] string invokedArgs,
            [MarshalAs(UnmanagedType.LPArray)] NotificationUserInputData[] data,
            uint dataCount);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotificationUserInputData
    {
        [MarshalAs(UnmanagedType.LPWStr)]
        public string Key;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string Value;
    }

    /// <summary>The actual COM object Windows creates/calls into when the toast body is clicked.</summary>
    [ComVisible(true)]
    [Guid(ClsidString)]
    [ClassInterface(ClassInterfaceType.None)]
    private sealed class ActivatorComObject : INotificationActivationCallback
    {
        public void Activate(string appUserModelId, string invokedArgs, NotificationUserInputData[] data, uint dataCount)
        {
            OnActivated(invokedArgs);
        }
    }

    [ComImport, Guid("00000001-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IClassFactory
    {
        [PreserveSig] int CreateInstance(IntPtr pUnkOuter, ref Guid riid, out IntPtr ppvObject);
        [PreserveSig] int LockServer([MarshalAs(UnmanagedType.Bool)] bool fLock);
    }

    private sealed class ClassFactory(Func<object> createInstance) : IClassFactory
    {
        private const int CLASS_E_NOAGGREGATION = unchecked((int)0x80040110);

        public int CreateInstance(IntPtr pUnkOuter, ref Guid riid, out IntPtr ppvObject)
        {
            ppvObject = IntPtr.Zero;
            if (pUnkOuter != IntPtr.Zero)
            {
                return CLASS_E_NOAGGREGATION;
            }

            var instance = createInstance();
            var unknown = Marshal.GetIUnknownForObject(instance);
            try
            {
                var hr = Marshal.QueryInterface(unknown, in riid, out ppvObject);
                return hr;
            }
            finally
            {
                Marshal.Release(unknown);
            }
        }

        public int LockServer(bool fLock) => 0;
    }
}
