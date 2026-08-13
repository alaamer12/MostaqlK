using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using MostaqlK.Core;
using MostaqlK.Models;
using MostaqlK.Services.Diagnostics;

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

    /// <summary>
    /// FIX ("not a single notification, ever"): registration used to happen lazily, the first
    /// time <see cref="SendAsync"/> ran - which for the default EndOfMinute grouping mode could
    /// be minutes after launch, on a background worker thread, and (per Microsoft's documented
    /// unpackaged-app flow) *after* the app could have already asked for/handled its own
    /// activation args. Call this once, as early as possible in the app's startup path (see
    /// <c>App.xaml.cs</c>'s constructor), so the AUMID + Start Menu shortcut + COM registration
    /// are in place well before the first real toast is ever attempted. Still safe/idempotent to
    /// call again from <see cref="SendAsync"/> itself in case startup registration is ever skipped.
    /// </summary>
    public static void EnsureRegisteredEagerly() => EnsureRegistered();

    [ErrorOutcome(ErrorOutcome.Handled, Label = "Toast delivery failure surfaced as Result<bool>.Err")]
    public Task<Result<bool>> SendAsync(IReadOnlyList<ProjectSummary> projects, CancellationToken cancellationToken = default)
    {
        if (projects.Count == 0)
        {
            return Task.FromResult(Result<bool>.Ok(true));
        }

        try
        {
            EnsureRegistered();

            // FIX ("not a single notification, ever"): Show() used to run unconditionally even
            // when Windows itself has notifications turned off for this app/user/machine, in
            // which case Show() neither throws nor returns a failure - it just no-ops. That made
            // "disabled" and "delivered" indistinguishable from this class's point of view. Now
            // logged explicitly (via the InteractionLogger fix that stopped this from being
            // compiled out of Release builds) so a disabled setting is finally diagnosable
            // instead of looking identical to a silent bug.
            var setting = AppNotificationManager.Default.Setting;
            if (setting != AppNotificationSetting.Enabled)
            {
                InteractionLogger.Mark("WindowsToastSender.SendAsync", "B", new { Reason = "notifications-disabled", Setting = setting.ToString(), Count = projects.Count });
                return Task.FromResult(Result<bool>.Err(NotificationErrors.ToastDeliveryFailed(
                    new InvalidOperationException($"App notifications are disabled (AppNotificationManager.Default.Setting = {setting})."))));
            }

            var builder = projects.Count == 1
                ? BuildIndividualToast(projects[0])
                : BuildGroupedToast(projects);

            AppNotificationManager.Default.Show(builder.BuildNotification());

            InteractionLogger.Mark("WindowsToastSender.SendAsync", "A", new { Count = projects.Count });

            return Task.FromResult(Result<bool>.Ok(true));
        }
        catch (Exception ex)
        {
            // Surfaced clearly per the "no silently swallowed toast failures" requirement — the
            // caller (NotificationDispatcher.HandleFlush) fires this off without awaiting, so this
            // Fault log is the only place a failed toast delivery becomes visible.
            InteractionLogger.Fault("WindowsToastSender.SendAsync", ex, new { Count = projects.Count });
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

            // Unpackaged (WindowsPackageType=None) apps have no package identity, so
            // AppNotificationManager.Register() alone registers only the COM activation server —
            // without an explicit AUMID + a Start Menu shortcut carrying it, Windows silently
            // drops the toast instead of showing it. See ToastAumidRegistrar for details.
            ToastAumidRegistrar.EnsureRegistered();

            // Per Microsoft's documented app-notifications flow, NotificationInvoked must be
            // subscribed BEFORE calling Register() - this was previously never subscribed at all,
            // so a clicked toast had no in-process handler to reactivate/focus the window.
            AppNotificationManager.Default.NotificationInvoked += OnNotificationInvoked;
            AppNotificationManager.Default.Register();
            _registered = true;

            InteractionLogger.Mark("WindowsToastSender.EnsureRegistered", "A", new { Setting = AppNotificationManager.Default.Setting.ToString() });
        }
    }

    /// <summary>
    /// Brings the window back to the foreground when the user clicks a delivered toast. Windows
    /// launches/reactivates the process and raises this on the notification's own thread; no
    /// deep-link routing yet (see the TODOs on <see cref="BuildIndividualToast"/>/
    /// <see cref="BuildGroupedToast"/> for the args already carried for that future step).
    /// </summary>
    private static void OnNotificationInvoked(AppNotificationManager sender, AppNotificationActivatedEventArgs args)
    {
        InteractionLogger.Mark("WindowsToastSender.OnNotificationInvoked", "A", new { Arguments = args.Arguments });
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
