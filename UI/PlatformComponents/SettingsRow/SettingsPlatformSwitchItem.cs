using MostaqlK.Core.Sources;

namespace MostaqlK.UI.PlatformComponents.SettingsRow;

/// <summary>
/// One line of the <see cref="SettingsShape.PlatformSwitches"/> row: a platform, whether it is on,
/// and whether the system will let the user change it.
/// <para>
/// The composition builds these from <c>SourceRegistry</c> + <c>PlatformEnablement</c>, so the
/// renderer enumerates data and names no platform — the same rule the rest of the unit follows. The
/// locked case carries no string either: the row's own hint states the invariant once, instead of
/// every locked switch repeating a sentence.
/// </para>
/// </summary>
public sealed record SettingsPlatformSwitchItem(
    SourceId Source,
    string NameAr,
    bool IsEnabled,
    bool IsLocked,
    string AutomationId);
