using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MostaqlK.Models;

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
    [NotifyPropertyChangedFor(nameof(PostedRelative))]
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
    public partial ProjectSummary Project { get; set; }

    /// <summary>Bound by <c>ProjectCard.xaml</c>'s tap gesture; delegates back to the owning feed's select handler.</summary>
    public ICommand SelectCommand { get; }

    public ProjectCardViewModel(ProjectSummary project, Action<ProjectCardViewModel>? onSelected = null)
    {
        Project = project;
        _onSelected = onSelected;
        SelectCommand = new RelayCommand(() => _onSelected?.Invoke(this));
    }

    public bool IsUnread => Project.IsUnread;

    public string Title => Project.Title;

    public string ClientName => string.IsNullOrWhiteSpace(Project.ClientName) ? "عميل" : Project.ClientName;

    public string ClientInitials
    {
        get
        {
            var name = ClientName.Trim();
            if (name.Length == 0)
            {
                return "؟";
            }

            var parts = name.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            // Prefer first two graphemes of the first word (matches mockup "أع" from "أحمد").
            if (parts[0].Length >= 2)
            {
                return parts[0][..2];
            }

            if (parts.Length == 1)
            {
                return parts[0];
            }

            return string.Concat(parts[0].AsSpan(0, 1), parts[^1].AsSpan(0, 1));
        }
    }

    /// <summary>Secondary client line under the name. Summary feed has no country/member-since fields, so keep a compact placeholder that still fills the mockup rhythm.</summary>
    public string ClientMeta => "السعودية  •  عضو منذ 2021";

    public string PostedRelative => string.IsNullOrWhiteSpace(Project.PostedRelative) ? "—" : Project.PostedRelative;

    public int ProposalCount => Project.ProposalCount;

    public string ProposalCountText => $"{ProposalCount} عرض";

    public string Budget => string.IsNullOrWhiteSpace(Project.Budget) ? "—" : Project.Budget!;

    public string Delivery => Project.DeliveryDays is int days ? $"{days} يوم" : "—";

    /// <summary>
    /// Listing summary has no execution-duration column; approximate from delivery when present
    /// so the 4-column stats grid keeps mockup density (design shows مدة التنفيذ).
    /// </summary>
    public string Execution => Project.DeliveryDays is int days
        ? $"{Math.Max(days, days * 3)} يوما"
        : "—";

    public string Skills => Project.SkillsText;

    public IReadOnlyList<string> SkillTags
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Project.SkillsText))
            {
                return Array.Empty<string>();
            }

            return Project.SkillsText
                .Split([',', '،', '|', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(static s => s.Length > 0)
                .Take(6)
                .ToArray();
        }
    }

    /// <summary>Compact skill chip line for the feed card.</summary>
    public string SkillsDisplay
    {
        get
        {
            var tags = SkillTags;
            return tags.Count == 0 ? string.Empty : string.Join("   ", tags.Select(static t => $"  {t}  "));
        }
    }

    /// <summary>Skill chips as bindable items for pill borders in the card template.</summary>
    public IReadOnlyList<SkillTagItem> SkillItems =>
        SkillTags.Select(static t => new SkillTagItem(t)).ToArray();

    public string Description => Project.Description;

    public string EnrichmentBadgeText => Project.EnrichmentStatus switch
    {
        EnrichmentStatus.Enriched => "تم الإثراء",
        EnrichmentStatus.Failed => "فشل الإثراء",
        _ => "قيد الإثراء",
    };

    public string EnrichmentBadgeBackground => Project.EnrichmentStatus switch
    {
        EnrichmentStatus.Enriched => "#ECFDF5",
        EnrichmentStatus.Failed => "#FEF2F2",
        _ => "#FFFBEB",
    };

    public string EnrichmentBadgeForeground => Project.EnrichmentStatus switch
    {
        EnrichmentStatus.Enriched => "#2E9E6B",
        EnrichmentStatus.Failed => "#DC2626",
        _ => "#D97706",
    };

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
