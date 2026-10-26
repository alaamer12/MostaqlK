using MostaqlK.Core.Formatting;
using MostaqlK.Core.Sources;
using MostaqlK.Models;

namespace MostaqlK.Features.Projects.ViewModels;

/// <summary>
/// One schema-driven meta row of the details page: a label/value pair the
/// <see cref="ISourceDescriptor.SchemaMap"/> says to render, inside a named section, at a given
/// order, plus the <see cref="IsMoney"/> flag that tells the host to isolate a Latin-currency
/// figure in <c>FlowDirection=LeftToRight</c> (backend-ui-contract §7).
/// </summary>
/// <param name="Key">The SchemaMap field key this row was built from.</param>
/// <param name="Section">The SchemaMap section id the row belongs to (grouping key).</param>
/// <param name="Order">The SchemaMap render order (within the source, ascending).</param>
/// <param name="LabelAr">Arabic field label, taken from the field table (never a source literal).</param>
/// <param name="Value">The resolved display value. A row is only emitted when this is non-empty.</param>
/// <param name="IsMoney">True when <paramref name="Value"/> is a currency figure needing LTR isolation.</param>
public sealed record DetailMetaRow(
    string Key,
    string Section,
    int Order,
    string LabelAr,
    string Value,
    bool IsMoney);

/// <summary>
/// A group of <see cref="DetailMetaRow"/>s sharing one <see cref="ISourceDescriptor.SchemaMap"/>
/// <see cref="Section"/>, titled for the host. The details view-model builds these from the renderer's
/// flat, order-sorted rows (grouped by section, within-group order preserved); a source that has no
/// row for a section produces no group, so its header is never rendered empty.
/// </summary>
/// <param name="Section">The SchemaMap section id (grouping key).</param>
/// <param name="HeaderAr">Arabic section header, from <see cref="DetailSchemaRenderer.SectionTitleAr"/>.</param>
/// <param name="Rows">The section's rows in ascending <see cref="DetailMetaRow.Order"/>.</param>
public sealed record DetailSectionGroup(
    string Section,
    string HeaderAr,
    IReadOnlyList<DetailMetaRow> Rows);

/// <summary>
/// One big-value/tiny-label mini of the details page DECISION STRIP (the "2-second go/no-go scan",
/// backend-ui-contract §7 / multi-source-requirements §6). The strip is the fixed approved fact set —
/// الميزانية · مدة التسليم · العروض المقدمة · الحالة · نُشر — rendered from the loaded project's own
/// values; a fact is only emitted when its underlying value is present (absent ⇒ omitted, never a "—"
/// placeholder), so each source naturally shows the subset it carries with no per-source branch.
/// </summary>
/// <param name="Key">Stable key identifying the fact (for the accent/money styling switch).</param>
/// <param name="LabelAr">The approved tiny caption (mockup wording, e.g. "مدة التسليم").</param>
/// <param name="Value">The formatted display value.</param>
/// <param name="IsMoney">True when the value is a Latin-currency figure needing LTR isolation.</param>
/// <param name="IsAccent">True when the value paints in the positive/state (green) ink rather than plain ink.</param>
public sealed record DecisionFact(string Key, string LabelAr, string Value, bool IsMoney, bool IsAccent, int Column = 0);

/// <summary>
/// Turns a source's <see cref="ISourceDescriptor.SchemaMap"/> + a <see cref="ProjectDetails"/> into
/// the ordered, section-grouped meta rows the details page renders — the schema/capability-driven
/// composition behind backend-ui-contract §1 (SchemaMap UI-read), §7 (executor block + money
/// direction), replacing the old hand-authored field order.
/// <para>
/// Three rules, all data-driven (no <c>if (source == X)</c> anywhere):
/// 1. Which fields appear, in which section, in what order = the descriptor's <c>SchemaMap</c>.
/// 2. A field whose value is null/empty is OMITTED entirely (never a "—" placeholder row) — that
///    is what "capability truth" means for the nullable extras (attachments/supervised/owner stats).
/// 3. The executor row is additionally gated on <see cref="SourceCapabilities.HasExecutorInfo"/>:
///    absent capability = the row is never emitted, even if a value somehow existed.
/// </para>
/// <para>
/// Structural keys (title/description/skills/attachments) are NOT meta rows — the host renders those
/// sections specially; the presence of <c>skills</c>/<c>attachments</c> in the map is handled by the
/// view-model's own <see cref="ProjectDetailsViewModel"/> collections (empty ⇒ section not shown).
/// </para>
/// </summary>
public static class DetailSchemaRenderer
{
    /// <summary>Scalar field keys the renderer knows how to resolve to a label/value row.</summary>
    private static readonly HashSet<string> StructuralKeys =
    [
        "title", "description", "skills", "attachments",
    ];

    /// <summary>Field key → Arabic label. Section headers come from <see cref="SectionTitleAr"/>.</summary>
    private static readonly Dictionary<string, string> FieldLabels = new(StringComparer.Ordinal)
    {
        ["project_status"] = "حالة المشروع",
        ["published_date"] = "تاريخ النشر",
        ["duration"] = "مدة التنفيذ",
        ["budget"] = "الميزانية",
        ["proposal_count"] = "العروض المقدمة",
        ["owner_name"] = "صاحب المشروع",
        ["registration_date"] = "تاريخ التسجيل",
        ["hire_rate"] = "معدل التوظيف",
        ["open_projects_count"] = "المشاريع المفتوحة",
        ["in_progress_count"] = "قيد التنفيذ",
        ["ongoing_conversations"] = "التواصلات الجارية",
        ["executor_name"] = "المنفذ",
    };

    /// <summary>
    /// Arabic header for a SchemaMap <c>Section</c> id, used by the host to title a group. Unknown
    /// sections fall back to the raw id so a future source's new section never renders blank.
    /// </summary>
    public static string SectionTitleAr(string section) => section switch
    {
        "project" => "بطاقة المشروع",
        "owner" => "صاحب المشروع",
        "executor" => "معلومات المنفذ",
        "summary" => "الوصف",
        "skills" => "المهارات المطلوبة",
        "attachments" => "المرفقات",
        _ => section,
    };

    /// <summary>
    /// True when the currency suffix is a Latin/other non-Arabic symbol (nafezly "$") that reads
    /// mangled inside an RTL paragraph and so needs a <c>LeftToRight</c> isolation; Arabic suffixes
    /// ("ر.س") and an empty suffix render fine in the page's RTL flow. Pure function of descriptor
    /// data — no source id consulted (the §7 "descriptor-driven, not a site literal" rule).
    /// </summary>
    public static bool MoneyNeedsLtr(string? currencySuffix) =>
        !string.IsNullOrEmpty(currencySuffix)
        && !currencySuffix.Any(c => c is >= '٠' and <= 'ي' or >= '\u0600' and <= '\u06FF');

    /// <summary>
    /// Builds the ordered meta rows for one project against its source descriptor. Rows are returned
    /// in ascending <see cref="FieldSpec.Order"/>; the caller groups them by <see cref="DetailMetaRow.Section"/>
    /// for the section headers while keeping that within-group order.
    /// </summary>
    public static IReadOnlyList<DetailMetaRow> Render(ISourceDescriptor descriptor, ProjectDetails details)
    {
        var rows = new List<DetailMetaRow>();

        // Order defensively: the descriptor already seeds ascending order, but the render contract
        // ("order within a section by Order") must not depend on the seed's array order alone.
        foreach (var spec in descriptor.SchemaMap.OrderBy(s => s.Order))
        {
            if (StructuralKeys.Contains(spec.Key))
            {
                continue;
            }

            // §7 executor gate: the row is capability-gated. Mostaql (HasExecutorInfo=false) never
            // emits it even though the field is simply absent there too; nafezly emits it only when
            // the project actually carries an executor name (awarded projects), else it is omitted.
            if (spec.Key == "executor_name" && !descriptor.Capabilities.HasExecutorInfo)
            {
                continue;
            }

            string? value = ResolveValue(spec.Key, details);
            if (string.IsNullOrWhiteSpace(value))
            {
                // Absent/null field ⇒ row omitted entirely, never an empty label (rule 2).
                continue;
            }

            var isMoney = spec.Key == "budget" && MoneyNeedsLtr(descriptor.CurrencySuffix);
            rows.Add(new DetailMetaRow(
                spec.Key,
                spec.Section,
                spec.Order,
                FieldLabels.TryGetValue(spec.Key, out var label) ? label : spec.Key,
                value,
                isMoney));
        }

        return rows;
    }

    /// <summary>
    /// Resolves one field key to its display string, or <c>null</c> when the project carries no value
    /// for it (so the caller omits the row). Values come from the model, formatted through the shared
    /// <c>Core/Formatting</c> helpers exactly as the old hand-authored rows did — only the *which* and
    /// *order* now come from the schema, not the layout.
    /// </summary>
    private static string? ResolveValue(string key, ProjectDetails details) => key switch
    {
        "project_status" => NonEmpty(details.ProjectStatus),
        "published_date" => NonEmpty(details.PublishTimeText),
        "duration" => details.DeliveryDays is int days ? ArabicRelativeTime.Days(days) : null,
        "budget" => NonEmpty(details.Budget),
        "proposal_count" => details.ProposalCount > 0 ? ArabicProposalParser.Format(details.ProposalCount) : null,
        "owner_name" => NonEmpty(details.Owner?.Name),
        "registration_date" => NonEmpty(details.Owner?.RegisteredAt),
        "hire_rate" => details.Owner?.HiringRatePercent is double rate
            ? rate.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "%"
            : null,
        "open_projects_count" => details.Owner?.OpenProjectsCount is int open ? open.ToString() : null,
        "in_progress_count" => details.Owner?.InProgressProjectsCount is int inProgress ? inProgress.ToString() : null,
        "ongoing_conversations" => details.Owner?.OngoingCommunicationsCount is int ongoing ? ongoing.ToString() : null,
        "executor_name" => NonEmpty(details.ExecutorName),
        _ => null,
    };

    private static string? NonEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
