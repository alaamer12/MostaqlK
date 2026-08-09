using MostaqlK.Core;
using MostaqlK.Infrastructure.Notifications;
using MostaqlK.Models;

namespace MostaqlK.Services;

/// <inheritdoc cref="INotificationDispatcher"/>
public sealed class NotificationDispatcher : INotificationDispatcher
{
    private readonly WindowsToastSender _toastSender;
    private readonly NotificationGrouper _grouper;

    public NotificationDispatcher(WindowsToastSender toastSender, NotificationGrouper grouper)
    {
        _toastSender = toastSender;
        _grouper = grouper;
    }

    public Task<Result<bool>> NotifyNewProjectsAsync(IReadOnlyList<ProjectSummary> projects, CancellationToken cancellationToken = default)
    {
        // TODO: feed `projects` into `_grouper`, drain when ready, and send via `_toastSender`.
        throw new NotImplementedException();
    }
}
