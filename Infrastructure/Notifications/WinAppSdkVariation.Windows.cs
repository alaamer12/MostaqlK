using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using MostaqlK.Core;
using MostaqlK.Core.Formatting;
using MostaqlK.Core.Navigation;
using MostaqlK.Models;
using MostaqlK.Services.Diagnostics;
using Microsoft.Maui;

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

        if (args.Arguments.TryGetValue("openUrl", out var url) && !string.IsNullOrWhiteSpace(url))
        {
            NotificationUrlLauncher.OpenUrl(url, "WinAppSdkVariation.OnNotificationInvoked");
            return;
        }

        if (args.Arguments.TryGetValue("projectId", out var projectIdStr) && long.TryParse(projectIdStr, out var projectId))
        {
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                try
                {
                    var services = IPlatformApplication.Current?.Services;

                    // Restore window if needed
                    var appLifecycleService = services?.GetService<Services.AppLifecycleService>();
                    if (appLifecycleService is { IsInBackground: true })
                    {
                        // Raise restore requested via TrayIconService
                        var trayService = services?.GetService<UI.TrayIcon.TrayIconService>();
                        trayService?.OnOpen();
                    }

                    await AppRoutes.NavigateAsync(AppRoutes.ProjectDetails(projectId));
                }
                catch (Exception ex)
                {
                    InteractionLogger.Fault("WinAppSdkVariation.NavigateToProject", ex, new { ProjectId = projectId });
                }
            });
        }
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

            // AppNotificationBuilder doesn't have a direct Lang property in the builder, 
            // but we can ensure the XML carries it for proper RTL layout.
            var notification = builder.BuildNotification();
            var xml = notification.Payload;
            if (!xml.Contains("lang="))
            {
                // Inject lang attribute into the toast and visual tags
                xml = xml.Replace("<toast", "<toast lang='ar-SA'");
                xml = xml.Replace("<visual", "<visual lang='ar-SA'");
                notification = new AppNotification(xml);
            }

            AppNotificationManager.Default.Show(notification);

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
        var originalDescription = project.Description ?? string.Empty;
        var description = TextTruncator.Truncate(originalDescription, 200);

        InteractionLogger.Mark("WinAppSdkVariation.BuildIndividualToast", "A", new 
        { 
            TitleLength = project.Title?.Length ?? 0,
            DescriptionLength = originalDescription.Length,
            TruncatedLength = description.Length
        });

        var builder = new AppNotificationBuilder()
            .SetAppLogoOverride(new Uri(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "Images", "logo.png")))
            .AddText(project.Title, new AppNotificationTextProperties().SetMaxLines(1))
            .AddText(description)
            .AddArgument("projectId", project.ProjectId.ToString());

        if (!string.IsNullOrWhiteSpace(project.Url))
        {
            builder.AddButton(new AppNotificationButton("عرض على مستقل")
                .AddArgument("openUrl", project.Url));
        }

        return builder;
    }


    private static AppNotificationBuilder BuildGroupedToast(IReadOnlyList<ProjectSummary> projects)
    {
        var header = $"يوجد {projects.Count} مشاريع جديدة — تفقدها هنا";
        var builder = new AppNotificationBuilder()
            .SetAppLogoOverride(new Uri(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "Images", "logo.png")))
            .AddText(header)
            .AddArgument("filter", "unread");

        var firstUrl = projects.Select(p => p.Url).FirstOrDefault(u => !string.IsNullOrWhiteSpace(u));
        if (!string.IsNullOrWhiteSpace(firstUrl))
        {
            builder.AddButton(new AppNotificationButton("عرض على مستقل")
                .AddArgument("openUrl", firstUrl));
        }

        return builder;
    }
}
