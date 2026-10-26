using System.Runtime.InteropServices;
using MostaqlK.Core.Navigation;
using MostaqlK.Core.Platform;
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
/// the source-link toast button uses protocol activation and is launched by the OS directly, so it did
/// not depend on this).
/// </para>
/// </summary>
public static class ToastActivator
{
    /// <summary>Fixed CLSID for this app's toast activation COM server. Must match the value
    /// written to the registry and stamped on the Start Menu shortcut by <see cref="ToastAumidRegistrar"/>.</summary>
    public const string ClsidString = ChannelIdentity.ToastActivatorClsid;

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
                if (services is null)
                {
                    InteractionLogger.Mark("ToastActivator.NavigateFromActivation", "NoServices");
                    return;
                }

                // Decide the target route FIRST, before touching the window, so a malformed project
                // toast can be ignored outright rather than restoring the window to a half-navigation.
                //
                // §6 (B5 hardening): a project-body click must resolve to ITS OWN source. The B-era
                // senders always stamp a `sourceId` argument on individual toasts, so a project click
                // whose sourceId is missing or unparsable is a stale/corrupt payload — falling back to
                // the default source here would silently open a DIFFERENT source's project that happens
                // to share the same site-local numeric id. Ignore the activation and log it instead of
                // navigating. This tightens ONLY the toast-argument path: external pre-v3 deep links keep
                // the documented route-level default (an absent sourceId still means the default source
                // in AppRoutes.ProjectDetails and ProjectDetailsPage.OnAppearing — both left unchanged).
                var target = AppRoutes.Projects;
                if (args.TryGetValue(AppRoutes.ProjectIdQueryParam, out var projectIdStr) && long.TryParse(projectIdStr, out var projectId))
                {
                    if (!args.TryGetValue(AppRoutes.SourceIdQueryParam, out var sourceStr)
                        || !MostaqlK.Core.Sources.SourceId.TryFrom(sourceStr, out var source))
                    {
                        InteractionLogger.Mark("ToastActivator.NavigateFromActivation", "Ignored-MissingSourceId", new { Arguments = invokedArgs, ProjectId = projectId });
                        return;
                    }

                    target = AppRoutes.ProjectDetails(source, projectId);
                }

                var appLifecycleService = services.GetService<Services.AppLifecycleService>();
                if (appLifecycleService is { IsInBackground: true })
                {
                    var trayService = services.GetService<UI.TrayIcon.TrayIconService>();
                    trayService?.OnOpen();
                }

                // §6 morph-first (owner decision): permanently morph to the clicked project's
                // source before navigating (no-op when already active). See SourceMorph.
                if (args.TryGetValue(AppRoutes.SourceIdQueryParam, out var morphStr)
                    && MostaqlK.Core.Sources.SourceId.TryFrom(morphStr, out var morphSource))
                {
                    MostaqlK.Services.SourceMorph.Apply(
                        services.GetService<MostaqlK.Core.Sources.ISettingsRepository>(),
                        services.GetService<Services.GlobalAppStatusService>(),
                        services.GetService<MostaqlK.UI.DesignSystem.BrandTokens.IBrandTokenProvider>(),
                        morphSource);
                }

                await AppRoutes.NavigateAsync(target);
            }
            catch (Exception ex)
            {
                InteractionLogger.Fault("ToastActivator.NavigateFromActivation", ex, new { Arguments = invokedArgs });
                MostaqlK.Services.Diagnostics.CrashReporter.Report("ToastActivator.NavigateFromActivation", ex, new { Arguments = invokedArgs }, isFatal: false);
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
        public void Activate(
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
        [PreserveSig] public int CreateInstance(IntPtr pUnkOuter, ref Guid riid, out IntPtr ppvObject);
        [PreserveSig] public int LockServer([MarshalAs(UnmanagedType.Bool)] bool fLock);
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
