using MostaqlK.Core.Sources;

namespace MostaqlK.Infrastructure.Notifications;

/// <summary>
/// User-facing toast strings that used to embed one website's name. The active source's own
/// display name comes from its module descriptor, so a toast never promises a site the
/// project did not come from.
/// <para>
/// Both senders read their copy from here. A grouped toast now carries exactly one platform's
/// projects, so the header says which one — the alternative was two senders each holding their own
/// Arabic literal, which is how the old single-site name survived in the first place.
/// </para>
/// </summary>
internal static class NotificationCopy
{
    /// <summary>Neutral stand-in when the registry cannot be reached (design-time only).</summary>
    public const string GenericSourceName = "المصدر";

    public static string ViewOnSource(SourceId source) => $"عرض على {NameOf(source)}";

    /// <summary>
    /// The grouped-toast header. Naming the source here is what the colors on the toast already
    /// assert, and it is the requirement <c>multi-source-requirements.md</c> §13 has carried since
    /// the wave began ("toasts must name their source").
    /// </summary>
    public static string GroupedHeader(SourceId source, int count) =>
        $"يوجد {count} مشاريع جديدة على {NameOf(source)} — تفقدها هنا";

    private static string NameOf(SourceId source)
    {
        var registry = IPlatformApplication.Current?.Services?.GetService<SourceRegistry>();
        return registry is not null && registry.TryGet(source, out var descriptor)
            ? descriptor.DisplayNameAr
            : GenericSourceName;
    }
}
