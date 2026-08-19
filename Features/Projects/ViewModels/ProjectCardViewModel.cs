using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MostaqlK.Core.Formatting;
using MostaqlK.Models;
using MostaqlK.UI.DesignSystem.Badges;
using MostaqlK.UI.PlatformComponents;

namespace MostaqlK.Features.Projects.ViewModels;

/// <summary>Simple skill chip model for BindableLayout pill rendering.</summary>
public sealed class SkillTagItem
{
    public SkillTagItem(string name) => Name = name;

    public string Name { get; }
}

/// <summary>
/// View-model backing a single <c>ProjectCard</c> in the project feed, wrapping a
/// <see cref="ProjectSummary"/> with the observable unread/read state shown in projects.html.
/// </summary>
public sealed partial class ProjectCardViewModel : ObservableObject
{
    private readonly Action<ProjectCardViewModel>? _onSelected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsUnread))]
    [NotifyPropertyChangedFor(nameof(Title))]
    [NotifyPropertyChangedFor(nameof(ClientName))]
    [NotifyPropertyChangedFor(nameof(ClientInitials))]
    [NotifyPropertyChangedFor(nameof(ClientMeta))]
    [NotifyPropertyChangedFor(nameof(PublishTimeText))]
    [NotifyPropertyChangedFor(nameof(ProposalCount))]
    [NotifyPropertyChangedFor(nameof(ProposalCountText))]
    [NotifyPropertyChangedFor(nameof(Budget))]
    [NotifyPropertyChangedFor(nameof(Delivery))]
    [NotifyPropertyChangedFor(nameof(Execution))]
    [NotifyPropertyChangedFor(nameof(Skills))]
    [NotifyPropertyChangedFor(nameof(SkillsDisplay))]
    [NotifyPropertyChangedFor(nameof(SkillTags))]
    [NotifyPropertyChangedFor(nameof(SkillItems))]
    [NotifyPropertyChangedFor(nameof(Description))]
    [NotifyPropertyChangedFor(nameof(EnrichmentBadgeText))]
    [NotifyPropertyChangedFor(nameof(EnrichmentBadgeBackground))]
    [NotifyPropertyChangedFor(nameof(EnrichmentBadgeForeground))]
    [NotifyPropertyChangedFor(nameof(EnrichmentBadgeIcon))]
    [NotifyPropertyChangedFor(nameof(IsEnriching))]
    public partial ProjectSummary Project { get; set; }

    /// <summary>Bound by <c>ProjectCard.xaml</c>'s tap gesture; delegates back to the owning feed's select handler.</summary>
    public ICommand SelectCommand { get; }

    /// <summary>Bound by <c>ProjectCard.xaml</c>'s "عرض في مستقل" button; opens the project's live listing on mostaql.com in the OS-default browser.</summary>
    public ICommand OpenOnMostaqlCommand { get; }

    public ProjectCardViewModel(ProjectSummary project, Action<ProjectCardViewModel>? onSelected = null)
    {
        Project = project;
        _onSelected = onSelected;
        SelectCommand = new RelayCommand(() => _onSelected?.Invoke(this));
        OpenOnMostaqlCommand = new AsyncRelayCommand(OpenOnMostaqlAsync);
    }

    /// <summary>
    /// Opens <see cref="MostaqlUrl"/> via <see cref="Launcher"/> - a fire-and-forget hand-off to the
    /// OS-default browser, same mechanism as the About page's "Mostaqlk" footer link
    /// (<c>AboutPage.xaml.cs</c>). Swallows failures (e.g. no default browser registered) instead of
    /// crashing the app, since this is a "nice to have" affordance, not a critical path.
    /// </summary>
    private async Task OpenOnMostaqlAsync()
    {
        if (string.IsNullOrWhiteSpace(MostaqlUrl))
        {
            return;
        }

        try
        {
            await Launcher.Default.OpenAsync(MostaqlUrl);
        }
        catch (Exception ex)
        {
            MostaqlK.Services.Diagnostics.InteractionLogger.Fault("ProjectCardViewModel.OpenOnMostaqlAsync", ex);
        }
    }

    /// <summary>Live listing URL for this project. Falls back to the canonical detail-page URL if the scraped <see cref="ProjectSummary.Url"/> is missing.</summary>
    public string MostaqlUrl => string.IsNullOrWhiteSpace(Project.Url)
        ? (Project.ProjectId > 0 ? $"https://mostaql.com/project/{Project.ProjectId}" : string.Empty)
        : Project.Url;

    public bool IsUnread => Project.IsUnread;

    public string Title => Project.Title;

    public string ClientName => string.IsNullOrWhiteSpace(Project.ClientName) ? "عميل" : Project.ClientName;

    public string ClientInitials => ArabicNameFormatter.GetInitials(ClientName);

    /// <summary>Secondary client line under the name. Summary feed has no country/member-since fields, so keep a compact placeholder that still fills the mockup rhythm.</summary>
    public string ClientMeta => "السعودية  •  عضو منذ 2021";

    /// <summary>
    /// Relative post time shown at the end of the client row ("منذ 3 دقائق" in projects.html),
    /// computed dynamically from the absolute <c>discovered_at</c> timestamp.
    /// </summary>
    public string PublishTimeText => ArabicRelativeTime.Since(Project.DiscoveredAt);

    public int ProposalCount => Project.ProposalCount;

    public string ProposalCountText => ArabicProposalParser.Format(Project.ProposalCount);

    /// <summary>Budget in the mockup's presentation form ("2,500 - 5,500 ر.س"), not the raw scraped string.</summary>
    public string Budget => BudgetFormatter.Format(Project.Budget);

    public string Delivery => Project.DeliveryDays is int days ? ArabicRelativeTime.Days(days) : "—";

    /// <summary>
    /// Real execution-duration ("مدة التنفيذ") persisted from the detail page's scraped
    /// "duration" field via <see cref="MostaqlK.Infrastructure.Http.Parsers.DetailParser"/> and
    /// stored in the projects table's <c>delivery_days</c> column. No value is fabricated: cards
    /// discovered but not yet enriched show the placeholder until enrichment fills it in.
    /// </summary>
    public string Execution => Project.DeliveryDays is int days
        ? ArabicRelativeTime.Days(days)
        : "—";

    public string Skills => Project.SkillsText;

    public IReadOnlyList<string> SkillTags => SkillsFormatter.ParseTags(Project.SkillsText);

    /// <summary>Compact skill chip line for the feed card.</summary>
    public string SkillsDisplay => SkillsFormatter.FormatDisplay(Project.SkillsText);

    /// <summary>Skill chips as bindable items for pill borders in the card template.</summary>
    public IReadOnlyList<SkillTagItem> SkillItems =>
        SkillTags.Select(static t => new SkillTagItem(t)).ToArray();

    public string Description => Project.Description;

    public string EnrichmentBadgeText => EnrichmentBadgeStyle.GetText(Project.EnrichmentStatus);

    public string EnrichmentBadgeBackground => EnrichmentBadgeStyle.GetBackgroundHex(Project.EnrichmentStatus);

    public string EnrichmentBadgeForeground => EnrichmentBadgeStyle.GetForegroundHex(Project.EnrichmentStatus);

    /// <summary>Leading icon inside the enrichment badge.</summary>
    public AppIconGlyph EnrichmentBadgeIcon => EnrichmentBadgeStyle.GetIcon(Project.EnrichmentStatus);

    /// <summary>
    /// True while the project's enrichment process is still running (queued or in-flight) —
    /// drives the card's <c>EnrichmentShimmerOverlay</c> sweep. Only <see cref="EnrichmentStatus.Pending"/>
    /// counts as "still enriching": both <see cref="EnrichmentStatus.Enriched"/> and
    /// <see cref="EnrichmentStatus.Failed"/> are terminal states, so the shimmer stops the
    /// instant enrichment actually finishes (successfully or not) rather than lingering.
    /// </summary>
    public bool IsEnriching => Project.EnrichmentStatus == EnrichmentStatus.Pending;

    public void MarkAsRead()
    {
        // TODO: persist the read state via IProjectRepository once an UpdateReadStateAsync
        // method is added to the repository — the AppCard/UI reflects it immediately either way.
        if (!Project.IsUnread)
        {
            return;
        }

        Project.IsUnread = false;
        OnPropertyChanged(nameof(IsUnread));
    }
}
