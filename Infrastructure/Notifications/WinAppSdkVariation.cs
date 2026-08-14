using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using MostaqlK.Core;
using MostaqlK.Models;
using MostaqlK.Services.Diagnostics;

namespace MostaqlK.Infrastructure.Notifications;

/// <summary>
/// Modern Windows App SDK-based toast notification variation.
/// Requires the "Singleton" MSIX package to be correctly provisioned on the machine.
/// </summary>
public sealed class WinAppSdkVariation : IToastVariation
{
    private bool _registered;

    public void EnsureRegistered()
    {
        if (_registered) return;

        // Unpackaged apps still need the AUMID and shortcut for identity.
        ToastAumidRegistrar.EnsureRegistered();
        
        // Subscription MUST happen before Register() per documentation.
        AppNotificationManager.Default.NotificationInvoked += OnNotificationInvoked;
        AppNotificationManager.Default.Register();
        _registered = true;

        InteractionLogger.Mark("WinAppSdkVariation.EnsureRegistered", "A", new { Setting = AppNotificationManager.Default.Setting.ToString() });
    }

    private static void OnNotificationInvoked(AppNotificationManager sender, AppNotificationActivatedEventArgs args)
    {
        InteractionLogger.Mark("WinAppSdkVariation.OnNotificationInvoked", "A", new { Arguments = args.Arguments });
    }

    public Task<Result<bool>> SendAsync(IReadOnlyList<ProjectSummary> projects)
    {
        try
        {
            var setting = AppNotificationManager.Default.Setting;
            if (setting != AppNotificationSetting.Enabled)
            {
                InteractionLogger.Mark("WinAppSdkVariation.SendAsync", "B", new { Reason = "notifications-disabled", Setting = setting.ToString(), Count = projects.Count });
                return Task.FromResult(Result<bool>.Err(NotificationErrors.ToastDeliveryFailed(
                    new InvalidOperationException($"App notifications are disabled (AppNotificationManager.Default.Setting = {setting})."))));
            }

            var builder = projects.Count == 1
                ? BuildIndividualToast(projects[0])
                : BuildGroupedToast(projects);

            AppNotificationManager.Default.Show(builder.BuildNotification());

            InteractionLogger.Mark("WinAppSdkVariation.SendAsync", "A", new { Count = projects.Count });
            return Task.FromResult(Result<bool>.Ok(true));
        }
        catch (Exception ex)
        {
            InteractionLogger.Fault("WinAppSdkVariation.SendAsync", ex, new { Count = projects.Count });
            return Task.FromResult(Result<bool>.Err(NotificationErrors.ToastDeliveryFailed(ex)));
        }
    }

    private static AppNotificationBuilder BuildIndividualToast(ProjectSummary project)
    {
        var builder = new AppNotificationBuilder()
            .AddText(project.Title)
            .AddText(BuildIndividualSubtitle(project))
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
        if (!string.IsNullOrWhiteSpace(project.ClientName)) parts.Add(project.ClientName);
        if (!string.IsNullOrWhiteSpace(project.PostedRelative)) parts.Add(project.PostedRelative);
        if (project.ProposalCount > 0) parts.Add($"{project.ProposalCount} عرض");
        return string.Join(" · ", parts);
    }

    private static AppNotificationBuilder BuildGroupedToast(IReadOnlyList<ProjectSummary> projects)
    {
        var header = $"يوجد {projects.Count} مشاريع جديدة — تفقدها هنا";
        var builder = new AppNotificationBuilder()
            .AddText(header)
            .AddArgument("filter", "unread");

        foreach (var project in projects.Take(2))
        {
            builder.AddText(project.Title);
        }

        return builder;
    }
}
