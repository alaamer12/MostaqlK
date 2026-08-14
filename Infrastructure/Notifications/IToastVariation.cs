using MostaqlK.Core;
using MostaqlK.Models;

namespace MostaqlK.Infrastructure.Notifications;

/// <summary>
/// Defines a variation of the Windows toast notification implementation.
/// Part of the "winToast-logic" refactoring to support multiple notification backends.
/// </summary>
public interface IToastVariation
{
    /// <summary>
    /// Performs one-time registration or initialization for this variation.
    /// </summary>
    void EnsureRegistered();

    /// <summary>
    /// Sends a toast notification for the specified batch of projects.
    /// </summary>
    Task<Result<bool>> SendAsync(IReadOnlyList<ProjectSummary> projects);
}
