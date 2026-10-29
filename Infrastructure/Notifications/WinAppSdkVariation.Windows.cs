using Microsoft.Maui;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using MostaqlK.Core;
using MostaqlK.Core.Formatting;
using MostaqlK.Core.Navigation;
using MostaqlK.Core.Notifications;
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

        if (args.Arguments.TryGetValue("openUrl", out var url) && !string.IsNullOrWhiteSpace(url))
        {
            NotificationUrlLauncher.OpenUrl(url, "WinAppSdkVariation.OnNotificationInvoked");
            return;
        }

        if (args.Arguments.TryGetValue("projectId", out var projectIdStr) && long.TryParse(projectIdStr, out var projectId))
        {
            // §6 (B5 hardening): mirror ToastActivator — resolve the project's OWN source from the
            // sourceId argument and IGNORE the click when it is missing/unparsable rather than
            // silently defaulting (a cross-source numeric-id collision would open the wrong project).
            // Validate before marshalling to the main thread so the window is never disturbed by a
            // malformed payload. Route-level defaults for external deep links are intentionally
            // unchanged (AppRoutes / ProjectDetailsPage).
            if (!TryReadSource(args.Arguments, out var source))
            {
                InteractionLogger.Mark("WinAppSdkVariation.OnNotificationInvoked", "Ignored-MissingSourceId", new { ProjectId = projectId });
                return;
            }

            MainThread.BeginInvokeOnMainThread(async () =>
            {
                try
                {
                    await RevealAndMorphAsync(source);
                    await AppRoutes.NavigateAsync(AppRoutes.ProjectDetails(source, projectId));
                }
                catch (Exception ex)
                {
                    InteractionLogger.Fault("WinAppSdkVariation.NavigateToProject", ex, new { ProjectId = projectId });
                }
            });
            return;
        }

        // A grouped toast carries no projectId, and used to fall through this handler doing nothing:
        // no window restore, no morph, no navigation — while the classic WinRT path has always opened
        // the projects feed for the same payload. Both backends now agree, and since dev66 a grouped
        // batch has exactly one source, so its click morphs to that source first.
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            try
            {
                if (TryReadSource(args.Arguments, out var groupedSource))
                {
                    await RevealAndMorphAsync(groupedSource);
                }
                else
                {
                    await RevealAsync();
                }

                await AppRoutes.NavigateAsync(AppRoutes.Projects);
            }
            catch (Exception ex)
            {
                InteractionLogger.Fault("WinAppSdkVariation.NavigateToProjects", ex);
            }
        });
    }

    /// <summary>
    /// §6 morph-first (owner decision): a notification from another source PERMANENTLY switches the
    /// app to that source before navigating, so the details page, palette, feed and live pipeline
    /// consumers all agree with what the user clicked. No-op when already on that source.
    /// </summary>
    private static async Task RevealAndMorphAsync(MostaqlK.Core.Sources.SourceId source)
    {
        await RevealAsync();

        var services = IPlatformApplication.Current?.Services;
        MostaqlK.Services.SourceMorph.Apply(
            services?.GetService<MostaqlK.Core.Sources.ISettingsRepository>(),
            services?.GetService<Services.GlobalAppStatusService>(),
            services?.GetService<MostaqlK.UI.DesignSystem.BrandTokens.IBrandTokenProvider>(),
            source);
    }

    /// <summary>
    /// The activation payload's source, if it carries one. A grouped toast has always been allowed to
    /// arrive without it (it simply does not morph); an individual project toast may not, because
    /// guessing a source would open a same-numbered project on the wrong site.
    /// </summary>
    private static bool TryReadSource(IDictionary<string, string> arguments, out MostaqlK.Core.Sources.SourceId source)
    {
        source = default;
        return arguments.TryGetValue("sourceId", out var value)
            && MostaqlK.Core.Sources.SourceId.TryFrom(value, out source);
    }

    /// <summary>Brings the window back from the tray, if it was hidden.</summary>
    private static Task RevealAsync()
    {
        var services = IPlatformApplication.Current?.Services;
        if (services?.GetService<Services.AppLifecycleService>() is { IsInBackground: true })
        {
            services.GetService<UI.TrayIcon.TrayIconService>()?.OnOpen();
        }

        return Task.CompletedTask;
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

            // The builder owns the text, arguments and buttons; the branded images are applied to its
            // payload in XML, which is the same seam the WinRT backend uses — so both APIs show one
            // source's toast identically, and this file already rebuilt the payload for the lang
            // attribute.
            var notification = builder.BuildNotification();
            var xml = ToastBranding.ApplyToBindingXml(notification.Payload, ToastBrandingResolver.For(projects[0].Source));
            if (!xml.Contains("lang="))
            {
                // Inject lang attribute into the toast and visual tags
                xml = xml.Replace("<toast", "<toast lang='ar-SA'");
                xml = xml.Replace("<visual", "<visual lang='ar-SA'");
            }

            AppNotificationManager.Default.Show(new AppNotification(xml));

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
            .AddText(project.Title, new AppNotificationTextProperties().SetMaxLines(1))
            .AddText(description)
            .AddArgument("projectId", project.ProjectId.ToString());

        // §6 (B5): every individual toast now carries its source id explicitly, including the default
        // source. Toast activation resolves the project's OWN source from this argument and no longer
        // falls back to the default, so a stale/garbled payload is ignored rather than mis-routed. Only
        // the raw toast ARGUMENT gained its source; the navigated ROUTE is unchanged for mostaql (its
        // AppRoutes.ProjectDetails still omits `&sourceId=`, see contract §6 first row).
        builder.AddArgument("sourceId", project.Source.Value);

        if (!string.IsNullOrWhiteSpace(project.Url))
        {
            builder.AddButton(new AppNotificationButton(NotificationCopy.ViewOnSource(project.Source))
                .AddArgument("openUrl", project.Url));
        }

        return builder;
    }

    private static AppNotificationBuilder BuildGroupedToast(IReadOnlyList<ProjectSummary> projects)
    {
        // One producing source per grouped batch (NotificationGrouper buckets per source), so the
        // header names it and the activation arguments carry it — which is what makes the existing
        // morph-first path fire for a grouped click as well as an individual one.
        var source = projects[0].Source;
        var header = NotificationCopy.GroupedHeader(source, projects.Count);
        var builder = new AppNotificationBuilder()
            .AddText(header)
            .AddArgument("filter", "unread")
            .AddArgument("sourceId", source.Value);

        var firstUrlProject = projects.FirstOrDefault(p => !string.IsNullOrWhiteSpace(p.Url));
        if (firstUrlProject is not null)
        {
            builder.AddButton(new AppNotificationButton(NotificationCopy.ViewOnSource(firstUrlProject.Source))
                .AddArgument("openUrl", firstUrlProject.Url));
        }

        return builder;
    }
}
