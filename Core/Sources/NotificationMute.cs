namespace MostaqlK.Core.Sources;

/// <summary>
/// Whether a platform's new projects may produce a toast right now — the one place that answers it.
/// <para>
/// <c>تنبيهات هذه المنصة</c> existed as a persisted row that nothing ever read: switching it off
/// changed a number on disk while every toast still arrived. The rule lives here (rather than inline
/// at the call site) so the settings row, the dispatcher, and the offline test all describe the same
/// container and the same default.
/// </para>
/// <para>
/// A second, unconditional authority arrives with the platform switches (a disabled platform emits
/// nothing at all); it is asked NEXT TO this one, never merged into it, because "this platform's
/// popups are muted" and "this platform is switched off" are different facts with different copy.
/// </para>
/// </summary>
public static class NotificationMute
{
    /// <summary>Default ON: a platform that was never configured still notifies, exactly as it did before the row existed.</summary>
    public static bool IsMuted(this ISettingsRepository settings, SourceId source) =>
        !settings.Get(source, SettingsKeys.EnableSourceNotifications, true);
}
