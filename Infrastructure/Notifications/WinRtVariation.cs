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

        // Classic WinRT toasts have no built-in click event for Win32 apps; without this COM
        // activator registered, clicking the toast body silently does nothing (see ToastActivator).
        ToastActivator.Register();
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
        var originalDescription = project.Description ?? string.Empty;
        var description = originalDescription.Length > 200 
            ? originalDescription.Substring(0, 197) + "..." 
            : originalDescription;

        InteractionLogger.Mark("WinRtVariation.BuildIndividualToastXml", "A", new 
        { 
            TitleLength = project.Title?.Length ?? 0,
            DescriptionLength = originalDescription.Length,
            TruncatedLength = description.Length
        });

        var actionsXml = string.IsNullOrWhiteSpace(project.Url)
            ? string.Empty
            : $@"<actions>
        <action content='عرض على مستقل' 
                arguments='openUrl={Uri.EscapeDataString(project.Url)}' />
    </actions>";

        var toastXmlString = $@"
<toast lang='ar-SA' launch='projectId={project.ProjectId}'>
    <visual lang='ar-SA'>
        <binding template='ToastGeneric'>
            <text>{System.Security.SecurityElement.Escape(project.Title)}</text>
            <text>{System.Security.SecurityElement.Escape(description)}</text>
        </binding>
    </visual>
    {actionsXml}
</toast>";

        var xml = new XmlDocument();
        xml.LoadXml(toastXmlString);
        return xml;
    }


    private static XmlDocument BuildGroupedToastXml(IReadOnlyList<ProjectSummary> projects)
    {
        var header = $"يوجد {projects.Count} مشاريع جديدة — تفقدها هنا";

        var firstUrl = projects.Select(p => p.Url).FirstOrDefault(u => !string.IsNullOrWhiteSpace(u));
        var actionsXml = string.IsNullOrWhiteSpace(firstUrl)
            ? string.Empty
            : $@"<actions>
        <action content='عرض على مستقل' 
                arguments='openUrl={Uri.EscapeDataString(firstUrl!)}' />
    </actions>";

        var toastXmlString = $@"
<toast lang='ar-SA' launch='filter=unread'>
    <visual lang='ar-SA'>
        <binding template='ToastGeneric'>
            <text>{System.Security.SecurityElement.Escape(header)}</text>
        </binding>
    </visual>
    {actionsXml}
</toast>";

        var xml = new XmlDocument();
        xml.LoadXml(toastXmlString);
        return xml;
    }
}
