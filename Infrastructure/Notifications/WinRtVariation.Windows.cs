using MostaqlK.Core;
using MostaqlK.Core.Formatting;
using MostaqlK.Models;
using MostaqlK.Services.Diagnostics;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

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
        var description = TextTruncator.Truncate(originalDescription, 200);

        InteractionLogger.Mark("WinRtVariation.BuildIndividualToastXml", "A", new
        {
            TitleLength = project.Title?.Length ?? 0,
            DescriptionLength = originalDescription.Length,
            TruncatedLength = description.Length
        });

        var actionsXml = string.IsNullOrWhiteSpace(project.Url)
            ? string.Empty
            : $@"<actions>
        <action content='{NotificationCopy.ViewOnSource(project.Source)}' 
                arguments='openUrl={Uri.EscapeDataString(project.Url)}' />
    </actions>";

        var logoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "Images", "logo.png");

        // Only non-default sources append a parameter, so mostaql launch strings stay byte-
        // identical to older builds (an absent sourceId resolves to the default source).
        var sourceParam = project.Source == MostaqlK.Core.Sources.SourceId.Default
            ? string.Empty
            : $"&amp;sourceId={Uri.EscapeDataString(project.Source.Value)}";

        var toastXmlString = $@"
<toast lang='ar-SA' launch='projectId={project.ProjectId}{sourceParam}'>
    <visual lang='ar-SA'>
        <binding template='ToastGeneric'>
            <image placement='appLogoOverride' src='file:///{logoPath}' />
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

        var firstUrlProject = projects.FirstOrDefault(p => !string.IsNullOrWhiteSpace(p.Url));
        var firstUrl = firstUrlProject?.Url;
        var actionsXml = string.IsNullOrWhiteSpace(firstUrl)
            ? string.Empty
            : $@"<actions>
        <action content='{NotificationCopy.ViewOnSource(firstUrlProject!.Source)}' 
                arguments='openUrl={Uri.EscapeDataString(firstUrl!)}' />
    </actions>";

        var logoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "Images", "logo.png");

        var toastXmlString = $@"
<toast lang='ar-SA' launch='filter=unread'>
    <visual lang='ar-SA'>
        <binding template='ToastGeneric'>
            <image placement='appLogoOverride' src='file:///{logoPath}' />
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
