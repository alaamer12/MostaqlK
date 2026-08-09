using MostaqlK.Core;
using MostaqlK.Models;

namespace MostaqlK.Services;

/// <summary>
/// Sends grouped or single-project notifications to the user (Windows toasts in V1).
/// </summary>
public interface INotificationDispatcher
{
    Task<Result<bool>> NotifyNewProjectsAsync(IReadOnlyList<ProjectSummary> projects, CancellationToken cancellationToken = default);

    /// <summary>
    /// Bounded, newest-first in-memory history of recently dispatched notifications. Backs the
    /// recent-notifications flyout (see <c>NotificationCenterViewModel</c>).
    /// </summary>
    IReadOnlyList<ProjectSummary> RecentHistory { get; }

    /// <summary>Raised whenever <see cref="RecentHistory"/> changes.</summary>
    event Action? HistoryChanged;
}
