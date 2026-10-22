using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MostaqlK.Core.Formatting;
using MostaqlK.Core.Sources;
using MostaqlK.Models;
using MostaqlK.Services;
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
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(HasStatusText))]
    [NotifyPropertyChangedFor(nameof(ProposalCount))]
    [NotifyPropertyChangedFor(nameof(ProposalCountText))]
    [NotifyPropertyChangedFor(nameof(Budget))]
    [NotifyPropertyChangedFor(nameof(BudgetIsForeignCurrency))]
    [NotifyPropertyChangedFor(nameof(Delivery))]
    [NotifyPropertyChangedFor(nameof(Execution))]
    [NotifyPropertyChangedFor(nameof(Skills))]
    [NotifyPropertyChangedFor(nameof(SkillsDisplay))]
    [NotifyPropertyChangedFor(nameof(SkillTags))]
    [NotifyPropertyChangedFor(nameof(SkillItems))]
    [NotifyPropertyChangedFor(nameof(HasSkills))]
    [NotifyPropertyChangedFor(nameof(Description))]
    [NotifyPropertyChangedFor(nameof(EnrichmentBadgeText))]
    [NotifyPropertyChangedFor(nameof(EnrichmentBadgeBackground))]
    [NotifyPropertyChangedFor(nameof(EnrichmentBadgeForeground))]
    [NotifyPropertyChangedFor(nameof(EnrichmentBadgeSoftBackground))]
    [NotifyPropertyChangedFor(nameof(EnrichmentBadgeIcon))]
    [NotifyPropertyChangedFor(nameof(IsEnriching))]
    public partial ProjectSummary Project { get; set; }

    /// <summary>Bound by <c>ProjectCard.xaml</c>'s tap gesture; delegates back to the owning feed's select handler.</summary>
    public ICommand SelectCommand { get; }

    /// <summary>Bound by <c>ProjectCard.xaml</c>'s source-link chip; opens the project's live listing on its own source site in the OS-default browser.</summary>
    public ICommand OpenOnSourceCommand { get; }

    // -----------------------------------------------------------------
    // v3 read-model row state (SourceFeedStore doctrine: the item ALWAYS exists — a row is never
    // removed because its data failed; it changes state). The W3 feed wave binds the visual
    // affordances (skeleton shimmer shape for Provisional, dim for Stale, broken placeholder +
    // retry for Broken); the data surface lands here first so XAML never rewrites bindings twice.
    // -----------------------------------------------------------------

    /// <summary>The row's lifecycle state within the feed snapshot.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsProvisional))]
    [NotifyPropertyChangedFor(nameof(IsStale))]
    [NotifyPropertyChangedFor(nameof(IsBroken))]
    [NotifyPropertyChangedFor(nameof(ShowsNormalContent))]
    public partial FeedRowState RowState { get; set; } = FeedRowState.Confirmed;

    /// <summary>True while the row is a provisional skeleton/optimistic shell (no confirmed data yet).</summary>
    public bool IsProvisional => RowState == FeedRowState.Provisional;

    /// <summary>True while confirmed data is shown dimmed, awaiting refine (stale-while-revalidate).</summary>
    public bool IsStale => RowState == FeedRowState.Stale;

    /// <summary>True when the underlying query failed — render as an honest broken placeholder with retry.</summary>
    public bool IsBroken => RowState == FeedRowState.Broken;

    /// <summary>
    /// W3 continuous-row renderer gate: the full data band (title/badges/skills/facts) is shown only
    /// for a Confirmed or Stale row. Provisional paints a skeleton and Broken paints a single-line
    /// red-ghost row, so this collapses the normal content for both. Pure display glue over the
    /// dev29 <see cref="FeedRowState"/> contract — no store/DB re-coupling.
    /// </summary>
    public bool ShowsNormalContent => !IsProvisional && !IsBroken;

    /// <summary>Honest failure copy of a <see cref="IsBroken"/> row (null otherwise).</summary>
    public string? BrokenMessage { get; private set; }

    /// <summary>Re-enqueues this source's feed refresh — the Broken row's retry affordance (owned by the W3 card template).</summary>
    public ICommand RetryCommand { get; private set; } = new RelayCommand(static () => { });

    /// <summary>Factory for a feed row built from a store <see cref="FeedItem"/> (confirmed or shell).</summary>
    public static ProjectCardViewModel ForFeedItem(FeedItem item, Action<ProjectCardViewModel>? onSelected, Action? onRetry = null)
    {
        // Provisional/Broken shells still carry a summary object — the doctrine's "item always
        // exists" means every card has real binding data; the shell's title is the honest copy.
        var card = new ProjectCardViewModel(item.Summary ?? new ProjectSummary
        {
            Source = item.Source,
            ProjectId = item.ProjectId,
            Title = item.ErrorMessage ?? string.Empty,
        }, onSelected)
        {
            RowState = item.State,
            BrokenMessage = item.ErrorMessage,
        };

        if (onRetry is not null)
        {
            card.RetryCommand = new RelayCommand(onRetry);
        }

        return card;
    }

    /// <summary>Reconciles this reused card with a newer snapshot item of the same project (in-place, no view churn).</summary>
    public void UpdateFrom(FeedItem item)
    {
        if (item.Summary is { } summary)
        {
            Project = summary;
        }

        RowState = item.State;
        BrokenMessage = item.ErrorMessage;
    }

    public ProjectCardViewModel(ProjectSummary project, Action<ProjectCardViewModel>? onSelected = null)
    {
        Project = project;
        _onSelected = onSelected;
        SelectCommand = new RelayCommand(() => _onSelected?.Invoke(this));
        OpenOnSourceCommand = new AsyncRelayCommand(OpenOnSourceAsync);
    }

    /// <summary>
    /// Opens <see cref="SourceUrl"/> via <see cref="Launcher"/> - a fire-and-forget hand-off to the
    /// OS-default browser, same mechanism as the About page's "Mostaqlk" footer link
    /// (<c>AboutPage.xaml.cs</c>). Swallows failures (e.g. no default browser registered) instead of
    /// crashing the app, since this is a "nice to have" affordance, not a critical path.
    /// </summary>
    private async Task OpenOnSourceAsync()
    {
        if (string.IsNullOrWhiteSpace(SourceUrl))
        {
            return;
        }

        try
        {
            await Launcher.Default.OpenAsync(SourceUrl);
        }
        catch (Exception ex)
        {
            MostaqlK.Services.Diagnostics.InteractionLogger.Fault("ProjectCardViewModel.OpenOnSourceAsync", ex);
        }
    }

    /// <summary>Live listing URL for this project. Falls back to the source's canonical detail-page URL (built by the source's own descriptor) when the scraped <see cref="ProjectSummary.Url"/> is missing.</summary>
    public string SourceUrl => string.IsNullOrWhiteSpace(Project.Url)
        ? (Project.ProjectId > 0 ? Descriptor?.BuildProjectUrl(Project.ProjectId) ?? string.Empty : string.Empty)
        : Project.Url;

    /// <summary>The project's source descriptor, resolved from the app container (null only in design-time hosts).</summary>
    private ISourceDescriptor? Descriptor => SourceRegistryProvider.DescriptorFor(Project.Source);

    public bool IsUnread => Project.IsUnread;

    public string Title => Project.Title;

    public string ClientName => string.IsNullOrWhiteSpace(Project.ClientName) ? "عميل" : Project.ClientName;

    public string ClientInitials => ArabicNameFormatter.GetInitials(ClientName);

    /// <summary>
    /// Secondary client line under the name. Summary feed has no country/member-since fields,
    /// so this shows the source's fallback owner meta (a fixture until real per-source owner
    /// fields arrive in v3); "—" when no descriptor is resolvable.
    /// </summary>
    public string ClientMeta => Descriptor?.OwnerFallbackMeta ?? "—";

    /// <summary>
    /// Relative post time shown on the card (projects.html "منذ 3 دقائق"). The SITE's own scraped
    /// relative text wins when present (nafezly rows are old projects re-discovered today —
    /// "منذ ساعة" from the listing, not "منذ دقيقة" since our poll saw them); the dynamic recompute
    /// from <c>discovered_at</c> is the documented fallback for rows without scraped text
    /// (mostaql discovery rows / design-seeded cards).
    /// </summary>
    public string PublishTimeText => string.IsNullOrWhiteSpace(Project.PublishTimeText)
        ? ArabicRelativeTime.Since(Project.DiscoveredAt)
        : Project.PublishTimeText.Trim();

    /// <summary>
    /// Scraped project-state text shown VERBATIM (contract §7: "UI shows scraped text verbatim";
    /// nafezly مفتوح/تحت التنفيذ/مكتمل from the listing meta row). Presence-by-data: sources/rows
    /// whose listing carries no state render no status block at all — never a placeholder, never a
    /// source-id check.
    /// </summary>
    public string? StatusText => string.IsNullOrWhiteSpace(Project.ProjectStatus) ? null : Project.ProjectStatus.Trim();

    public bool HasStatusText => StatusText is not null;

    public int ProposalCount => Project.ProposalCount;

    public string ProposalCountText => ArabicProposalParser.Format(Project.ProposalCount);

    /// <summary>Budget in the mockup's presentation form (grouped amounts + the source's currency suffix), not the raw scraped string.</summary>
    public string Budget => BudgetFormatter.Format(Project.Budget, Descriptor?.CurrencySuffix ?? string.Empty);

    /// <summary>
    /// W3 LTR-isolation flag: only a foreign-currency ("$") budget run is directionally isolated to
    /// left-to-right in the otherwise-RTL row (requirements §11.4 — "$" numbers render LTR; ر.س stays
    /// RTL). Data-driven (presence of the symbol), never a source-id check.
    /// </summary>
    public bool BudgetIsForeignCurrency => Budget.Contains('$');

    public string Delivery => Project.DeliveryDays is int days ? ArabicRelativeTime.Days(days) : "—";

    /// <summary>
    /// Real execution-duration ("مدة التنفيذ") persisted from the detail page's scraped
    /// "duration" field via <see cref="MostaqlK.Sources.Mostaql.Parsing.DetailParser"/> and
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

    /// <summary>True when the row carries at least one skill chip (band-3 presence-by-data gate).</summary>
    public bool HasSkills => SkillTags.Count > 0;

    public string Description => Project.Description;

    public string EnrichmentBadgeText => EnrichmentBadgeStyle.GetText(Project.EnrichmentStatus);

    public string EnrichmentBadgeBackground => EnrichmentBadgeStyle.GetBackgroundHex(Project.EnrichmentStatus);

    public string EnrichmentBadgeForeground => EnrichmentBadgeStyle.GetForegroundHex(Project.EnrichmentStatus);

    /// <summary>
    /// W3 continuous-row enrichment pill background: the badge's own STATE colour at ~12% alpha
    /// (mockup <c>rgba(46,158,107,.1)</c> family), so the pill reads as a soft state tint over the
    /// row surface in BOTH themes instead of the flat light-only fill the opaque hex produces on
    /// dark. Derived purely from <see cref="EnrichmentBadgeForeground"/> — a display transform, no
    /// new palette, state colours stay non-brand (requirements §D6).
    /// </summary>
    public string EnrichmentBadgeSoftBackground
    {
        get
        {
            var fg = EnrichmentBadgeForeground;
            return fg.Length == 7 && fg[0] == '#' ? "#1F" + fg[1..] : fg;
        }
    }

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
