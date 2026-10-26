namespace MostaqlK.Core.Platform;

/// <summary>
/// Compile-time build channel. The stable build is the default; a canary build is stamped with
/// <c>CHANNEL_CANARY</c> (<c>-p:Channel=canary</c> in MostaqlK.csproj). Every persistent OS identity
/// — data root, named kernel objects, AUMID, toast COM CLSID, startup value — derives from here so a
/// stable and a canary build can run side by side without sharing state.
/// </summary>
public static class ChannelIdentity
{
#if CHANNEL_CANARY
    public const string Suffix = ".Canary";

    /// <summary>Distinct CLSID so each channel owns its own toast-activation COM server.</summary>
    public const string ToastActivatorClsid = "A1C0D5E2-7B34-4C8F-9E6A-2D7B4F1C8E30";

    public static readonly bool IsCanary = true;
#else
    public const string Suffix = "";

    /// <summary>Existing stable CLSID — must never change or toast clicks break for installed users.</summary>
    public const string ToastActivatorClsid = "6A6D9142-4F91-4C6A-9C7B-6B6A6E5D9A11";

    public static readonly bool IsCanary = false;
#endif

    /// <summary>Root identity and app-data folder name: <c>MostaqlK</c> or <c>MostaqlK.Canary</c>.</summary>
    public static readonly string AppName = "MostaqlK" + Suffix;

    /// <summary>AppUserModelID and Start Menu shortcut identity (<c>MostaqlK.App</c> / <c>MostaqlK.Canary.App</c>).</summary>
    public static readonly string Aumid = AppName + ".App";

    public static readonly string SingletonMutexName = @"Local\" + AppName + ".App.Singleton";
    public static readonly string WakeEventName = @"Local\" + AppName + ".App.Wake";
    public static readonly string AppInstanceKey = AppName + ".App.Singleton.Instance";
    public static readonly string WatchdogMutexName = @"Local\" + AppName + ".App.Watchdog";

    /// <summary>HKCU Run value name used by the startup toggle.</summary>
    public static readonly string StartupRunValueName = AppName;

    public static readonly string TrayIconTempDirectoryName = AppName + "_TrayIcons";
}
