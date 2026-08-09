using MostaqlK.Core;
using MostaqlK.Models;

namespace MostaqlK.Services;

/// <summary>
/// Sends grouped or single-project notifications to the user (Windows toasts in V1).
/// </summary>
public interface INotificationDispatcher
{
    Task<Result<bool>> NotifyNewProjectsAsync(IReadOnlyList<ProjectSummary> projects, CancellationToken cancellationToken = default);
}
