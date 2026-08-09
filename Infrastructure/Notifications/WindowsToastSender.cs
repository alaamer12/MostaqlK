using MostaqlK.Core;
using MostaqlK.Models;

namespace MostaqlK.Infrastructure.Notifications;

/// <summary>
/// Sends Windows toast notifications for newly discovered/enriched projects.
/// Windows-specific by nature; Android will get its own implementation in V3.
/// </summary>
public sealed class WindowsToastSender
{
    public Task<Result<bool>> SendAsync(IReadOnlyList<ProjectSummary> projects, CancellationToken cancellationToken = default)
    {
        // TODO: build and show a Windows toast (e.g. via CommunityToolkit.Maui or WinRT toast APIs).
        throw new NotImplementedException();
    }
}
