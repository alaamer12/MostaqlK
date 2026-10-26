using MostaqlK.Core.Sources;

namespace MostaqlK.Infrastructure.Notifications;

/// <summary>
/// User-facing toast strings that used to embed one website's name. The active source's own
/// display name comes from its module descriptor, so a toast never promises a site the
/// project did not come from.
/// </summary>
internal static class NotificationCopy
{
    /// <summary>Neutral stand-in when the registry cannot be reached (design-time only).</summary>
    public const string GenericSourceName = "المصدر";

    public static string ViewOnSource(SourceId source)
    {
        var registry = IPlatformApplication.Current?.Services?.GetService<SourceRegistry>();
        var name = registry is not null && registry.TryGet(source, out var descriptor)
            ? descriptor.DisplayNameAr
            : GenericSourceName;
        return $"عرض على {name}";
    }
}
