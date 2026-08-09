using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using MostaqlK.Core;
using MostaqlK.Models;

namespace MostaqlK.Infrastructure.Notifications;

/// <summary>
/// Sends Windows toast notifications for newly discovered/enriched projects.
/// Windows-specific by nature; Android will get its own implementation in V3.
/// Uses the Windows App SDK's <see cref="AppNotificationManager"/> (the modern, native toast API
/// for WinUI3 apps), which this project already brings in transitively via
/// <c>MauiWinUIApplication</c> — no extra package reference needed.
/// </summary>
public sealed class WindowsToastSender
{
    private static readonly object RegisterLock = new();
    private static bool _registered;

    public Task<Result<bool>> SendAsync(IReadOnlyList<ProjectSummary> projects, CancellationToken cancellationToken = default)
    {
        if (projects.Count == 0)
        {
            return Task.FromResult(Result<bool>.Ok(true));
        }

        try
        {
            EnsureRegistered();

            var builder = projects.Count == 1
                ? BuildIndividualToast(projects[0])
                : BuildGroupedToast(projects);

            AppNotificationManager.Default.Show(builder.BuildNotification());

            return Task.FromResult(Result<bool>.Ok(true));
        }
        catch (Exception ex)
        {
            return Task.FromResult(Result<bool>.Err(NotificationErrors.ToastDeliveryFailed(ex)));
        }
    }

    private static void EnsureRegistered()
    {
        if (_registered)
        {
            return;
        }

        lock (RegisterLock)
        {
            if (_registered)
            {
                return;
            }

            AppNotificationManager.Default.Register();
            _registered = true;
        }
    }

    /// <summary>
    /// Per system-components.md #12: title, owner name, time posted, proposal count (budget/category
    /// are added once <see cref="ProjectSummary"/> carries them through from enrichment).
    /// </summary>
    private static AppNotificationBuilder BuildIndividualToast(ProjectSummary project)
    {
        var builder = new AppNotificationBuilder()
            .AddText(project.Title)
            .AddText(BuildIndividualSubtitle(project))
            // TODO: deep-link into the main window scrolled/filtered to this project once
            // Features/UI routing exists (a later step) — for now the argument is carried on the
            // notification so that hook can be wired without touching this class again.
            .AddArgument("projectId", project.ProjectId.ToString());

        if (!string.IsNullOrWhiteSpace(project.Url))
        {
            builder.AddArgument("url", project.Url);
        }

        return builder;
    }

    private static string BuildIndividualSubtitle(ProjectSummary project)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(project.ClientName))
        {
            parts.Add(project.ClientName);
        }

        if (!string.IsNullOrWhiteSpace(project.PostedRelative))
        {
            parts.Add(project.PostedRelative);
        }

        if (project.ProposalCount > 0)
        {
            parts.Add($"{project.ProposalCount} عرض");
        }

        return string.Join(" · ", parts);
    }

    /// <summary>
    /// Per configuration-reference.md § Notification grouping / ui-ux-design.md § Notification
    /// grouping (UX side): "There are N new projects — check them here", clicking opens the
    /// window pre-filtered to unread.
    /// </summary>
    private static AppNotificationBuilder BuildGroupedToast(IReadOnlyList<ProjectSummary> projects)
    {
        var builder = new AppNotificationBuilder()
            .AddText($"يوجد {projects.Count} مشاريع جديدة — تفقدها هنا")
            // TODO: deep-link into the unread-filtered (`is_read = false`) project feed once
            // Features/UI routing exists — see the individual-toast TODO above for the same hook.
            .AddArgument("filter", "unread");

        // AppNotificationBuilder caps a toast at 3 text elements total; the header above already
        // uses one, so at most 2 project titles can be listed alongside it.
        foreach (var project in projects.Take(2))
        {
            builder.AddText(project.Title);
        }

        return builder;
    }
}
