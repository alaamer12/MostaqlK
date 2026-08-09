using MostaqlK.Models;

namespace MostaqlK.Services;

/// <summary>
/// Strategy used to batch newly enriched projects into a single toast rather than
/// firing one notification per project.
/// </summary>
public enum NotificationGroupingMode
{
    /// <summary>Flush whatever accumulated at the end of the current minute.</summary>
    EndOfMinute,

    /// <summary>Flush after a configurable number of minutes has elapsed since the first item.</summary>
    AfterMinutes,

    /// <summary>Flush as soon as a configurable item count is reached.</summary>
    AfterCount
}

/// <summary>
/// Buffers newly discovered/enriched projects and decides when a batch is ready to be
/// flushed to the <see cref="INotificationDispatcher"/> based on the configured grouping mode.
/// </summary>
public sealed class NotificationGrouper
{
    private readonly List<ProjectSummary> _pending = [];

    public NotificationGroupingMode Mode { get; set; } = NotificationGroupingMode.EndOfMinute;

    public int AfterMinutesThreshold { get; set; } = 1;

    public int AfterCountThreshold { get; set; } = 5;

    public void Add(ProjectSummary project)
    {
        _pending.Add(project);
    }

    /// <summary>
    /// Returns the pending batch and clears it if the configured threshold has been reached.
    /// Real timing/threshold logic is TODO.
    /// </summary>
    public IReadOnlyList<ProjectSummary> DrainIfReady()
    {
        // TODO: implement EndOfMinute / AfterMinutes / AfterCount timing logic.
        throw new NotImplementedException();
    }
}
