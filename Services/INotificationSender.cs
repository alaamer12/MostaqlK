using MostaqlK.Core;
using MostaqlK.Models;

namespace MostaqlK.Services;

/// <summary>
/// Platform-neutral boundary for delivering native notifications about newly discovered projects.
/// V1's only implementation is the Windows toast sender; mobile backends plug in later behind the
/// same contract without touching the dispatcher/grouper.
/// </summary>
public interface INotificationSender
{
    /// <summary>
    /// Delivers a toast (or platform-equivalent) for the given batch of projects.
    /// </summary>
    Task<Result<bool>> SendAsync(IReadOnlyList<ProjectSummary> projects, CancellationToken cancellationToken = default);
}
