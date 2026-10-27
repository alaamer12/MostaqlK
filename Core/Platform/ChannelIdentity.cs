namespace MostaqlK.Core.Platform;

/// <summary>
/// Compile-time build channel. The stable build is the default; CI selects the canary variant with
/// <c>-p:Channel=canary</c>, which compiles <c>ChannelIdentity.Canary.cs</c> instead of
/// <c>ChannelIdentity.Stable.cs</c> (per-variant files, never <c>#if</c> in bodies). Every
/// persistent OS identity — data root, named kernel objects, AUMID, toast COM CLSID, startup value
/// — derives from here so a stable and a canary build can run side by side without sharing state.
/// The channel axis is the only one that feeds OS identity (the two must coexist); sources and
/// settings scopes are data axes and never change these names.
/// </summary>
public static partial class ChannelIdentity
{
    // Seam fulfilled by ChannelIdentity.Stable.cs / ChannelIdentity.Canary.cs:
    //   const string Suffix                  — "" on stable, ".Canary" on canary
    //   const string ToastActivatorClsid     — per-channel toast COM server id
    //   static readonly bool IsCanary        — channel probe for guards and badges

    /// <summary>
    /// The channel-independent brand prefix. Only for logic that must recognize <em>any</em>
    /// channel's artifacts (e.g. the foreign bundle-cache guard); identity itself always uses
    /// <see cref="AppName"/>.
    /// </summary>
    public const string BaseAppName = "MostaqlK";

    /// <summary>Root identity and app-data folder name: <c>MostaqlK</c> or <c>MostaqlK.Canary</c>.</summary>
    public static readonly string AppName = BaseAppName + Suffix;

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
