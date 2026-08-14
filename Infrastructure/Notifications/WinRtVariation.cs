using Windows.UI.Notifications;
using Windows.Data.Xml.Dom;
using MostaqlK.Core;
using MostaqlK.Models;
using MostaqlK.Services.Diagnostics;

namespace MostaqlK.Infrastructure.Notifications;

/// <summary>
/// Robust WinRT-based toast notification variation.
/// Works reliably for unpackaged apps via Start Menu shortcut + AUMID.
/// </summary>
public sealed class WinRtVariation : IToastVariation
{
    private bool _registered;

    public void EnsureRegistered()
    {
        if (_registered) return;
        
        // Unpackaged apps MUST have a Start Menu shortcut with a matching AUMID for 
        // ToastNotificationManager to accept the notification.
        ToastAumidRegistrar.EnsureRegistered();
        _registered = true;
        
        InteractionLogger.Mark("WinRtVariation.EnsureRegistered", "A", new { Aumid = ToastAumidRegistrar.Aumid });
    }

    public Task<Result<bool>> SendAsync(IReadOnlyList<ProjectSummary> projects)
    {
        try
        {
            var toastXml = projects.Count == 1
                ? BuildIndividualToastXml(projects[0])
                : BuildGroupedToastXml(projects);

            var toast = new ToastNotification(toastXml);
            ToastNotificationManager.CreateToastNotifier(ToastAumidRegistrar.Aumid).Show(toast);

            InteractionLogger.Mark("WinRtVariation.SendAsync", "A", new { Count = projects.Count });
            return Task.FromResult(Result<bool>.Ok(true));
        }
        catch (Exception ex)
        {
            InteractionLogger.Fault("WinRtVariation.SendAsync", ex, new { Count = projects.Count });
            return Task.FromResult(Result<bool>.Err(NotificationErrors.ToastDeliveryFailed(ex)));
        }
    }

    private static XmlDocument BuildIndividualToastXml(ProjectSummary project)
    {
        var toastXmlString = $@"
<toast>
    <visual>
        <binding template='ToastGeneric'>
            <text>{System.Security.SecurityElement.Escape(project.Title)}</text>
            <text>{System.Security.SecurityElement.Escape(BuildIndividualSubtitle(project))}</text>
        </binding>
    </visual>
</toast>";

        var xml = new XmlDocument();
        xml.LoadXml(toastXmlString);
        return xml;
    }

    private static string BuildIndividualSubtitle(ProjectSummary project)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(project.ClientName)) parts.Add(project.ClientName);
        if (!string.IsNullOrWhiteSpace(project.PostedRelative)) parts.Add(project.PostedRelative);
        if (project.ProposalCount > 0) parts.Add($"{project.ProposalCount} عرض");
        return string.Join(" · ", parts);
    }

    private static XmlDocument BuildGroupedToastXml(IReadOnlyList<ProjectSummary> projects)
    {
        var header = $"يوجد {projects.Count} مشاريع جديدة — تفقدها هنا";
        var titles = string.Join("\n", projects.Take(2).Select(p => p.Title));

        var toastXmlString = $@"
<toast>
    <visual>
        <binding template='ToastGeneric'>
            <text>{System.Security.SecurityElement.Escape(header)}</text>
            <text>{System.Security.SecurityElement.Escape(titles)}</text>
        </binding>
    </visual>
</toast>";

        var xml = new XmlDocument();
        xml.LoadXml(toastXmlString);
        return xml;
    }
}
