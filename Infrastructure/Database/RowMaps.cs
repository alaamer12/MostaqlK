using System.Runtime.CompilerServices;
using Dapper;
using MostaqlK.Core.Sources;
using MostaqlK.Models;

namespace MostaqlK.Infrastructure.Database;

/// <summary>
/// Global Dapper configuration plus the database-shape DTOs and their explicit
/// DTO-to-domain mappers for the SQLite repositories.
/// </summary>
/// <remarks>
/// <para>
/// Each DTO mirrors one row projection exactly: one property per selected column, named in
/// snake_case to match the column name the provider reports, and typed to the stored value
/// (<see cref="long"/> internal ids, <see cref="string"/> ISO-8601 "O"-format dates,
/// <see cref="int"/>/<see cref="long"/> 0/1 flags, nullable for NULL-able columns). Dapper is
/// configured ONCE here via <see cref="DefaultTypeMap.MatchNamesWithUnderscores"/> so
/// snake_case columns bind to these properties without any alias churn in the SELECT lists;
/// the static constructor is force-run from <see cref="ConfigureDapper"/> (a module
/// initializer) so the setting is applied before any query in the assembly can build a
/// cached row-deserializer against the old value.
/// </para>
/// <para>
/// The mappers replicate the pre-Dapper <c>SqliteDataReader</c> mapping loops line-for-line
/// (null-coalescing defaults, <c>Enum.Parse</c> call sites, internal-id band subtraction via
/// <see cref="SourceRegistry"/>) so behavior is unchanged; the non-nullable <c>string</c>
/// members are initialized to <see cref="string.Empty"/> only to satisfy nullable analysis -
/// the corresponding columns are NOT NULL in the schema and the reader path that these
/// mappers replace would historically have thrown before ever observing a null there.
/// </para>
/// </remarks>
internal static class RowMaps
{
    static RowMaps()
    {
        // Snake_case DTO properties map to same-named columns; enabling underscore matching
        // makes the binding robust for any future PascalCase member and is documented above.
        // (Dapper 2.1.89 exposes this flag on Dapper.DefaultTypeMap, not SqlMapper.Settings.)
        DefaultTypeMap.MatchNamesWithUnderscores = true;
    }

    [ModuleInitializer]
    internal static void ConfigureDapper() =>
        RuntimeHelpers.RunClassConstructor(typeof(RowMaps).TypeHandle);

    /// <summary>(source_id, project_id) key pair - projects/backlog identity rows.</summary>
    internal sealed class ProjectKeyRow
    {
        public string source_id { get; set; } = string.Empty;
        public long project_id { get; set; }
    }

    /// <summary><c>GetRecentAsync</c> projection (project columns + skills_text + enriched_at).</summary>
    internal sealed class ProjectSummaryRow
    {
        public long project_id { get; set; }
        public string source_id { get; set; } = string.Empty;
        public string title { get; set; } = string.Empty;
        public string url { get; set; } = string.Empty;
        public string? client_name { get; set; }
        public int? publish_time_number { get; set; }
        public string? publish_time_text { get; set; }
        public int? proposal_count { get; set; }
        public string? proposal_count_text { get; set; }
        public string? description { get; set; }
        public string? budget { get; set; }
        public int? delivery_days { get; set; }
        public long? is_unread { get; set; }
        public string enrichment_status { get; set; } = string.Empty;
        public string discovered_at { get; set; } = string.Empty;
        public string? project_status { get; set; }
        public string? skills_text { get; set; }
        public string? enriched_at { get; set; }
    }

    /// <summary>
    /// projects LEFT JOIN owners projection (<c>GetDetailsAsync</c> / <c>GetAllDetailsAsync</c>);
    /// the <c>o.*</c> properties stay null when the join produces no owner row.
    /// </summary>
    internal sealed class ProjectDetailsRow
    {
        public long project_id { get; set; }
        public string source_id { get; set; } = string.Empty;
        public string title { get; set; } = string.Empty;
        public string url { get; set; } = string.Empty;
        public string? description { get; set; }
        public string? budget { get; set; }
        public int? delivery_days { get; set; }
        public string enrichment_status { get; set; } = string.Empty;
        public string? enriched_at { get; set; }
        public string? project_status { get; set; }
        public int? publish_time_number { get; set; }
        public string? publish_time_text { get; set; }
        public int? proposal_count { get; set; }
        public string? proposal_count_text { get; set; }
        public string? discovered_at { get; set; }
        public long? owner_id { get; set; }
        public string? name { get; set; }
        public string? profile_url { get; set; }
        public string? avatar_url { get; set; }
        public double? rating { get; set; }
        public int? completed_projects_count { get; set; }
        public double? hiring_rate_percent { get; set; }
        public string? registered_at { get; set; }
        public int? open_projects_count { get; set; }
        public int? in_progress_projects_count { get; set; }
        public int? ongoing_communications_count { get; set; }
    }

    /// <summary><c>owners</c> table row (<c>GetByIdAsync</c> / <c>GetAllAsync</c>).</summary>
    internal sealed class OwnerRow
    {
        public long owner_id { get; set; }
        public string source_id { get; set; } = string.Empty;
        public string name { get; set; } = string.Empty;
        public string? profile_url { get; set; }
        public string? avatar_url { get; set; }
        public double? rating { get; set; }
        public int? completed_projects_count { get; set; }
        public double? hiring_rate_percent { get; set; }
        public string? registered_at { get; set; }
        public int? open_projects_count { get; set; }
        public int? in_progress_projects_count { get; set; }
        public int? ongoing_communications_count { get; set; }
    }

    /// <summary><c>assets</c> table row (metadata only; no binary content is stored).</summary>
    internal sealed class AssetRow
    {
        public long asset_id { get; set; }
        public long project_id { get; set; }
        public string file_name { get; set; } = string.Empty;
        public string? url { get; set; }
        public string? raw_url { get; set; }
        public string? local_path { get; set; }
        public long? size_bytes { get; set; }
        public string? extension { get; set; }
        public long? requires_auth { get; set; }
        public string? size_text { get; set; }
    }

    /// <summary>
    /// <c>project_skills</c> row; <see cref="project_id"/> is only populated by the batch
    /// projection in <c>GetAllDetailsAsync</c> (the per-project query selects name/url only).
    /// </summary>
    internal sealed class SkillRow
    {
        public long project_id { get; set; }
        public string name { get; set; } = string.Empty;
        public string? url { get; set; }
    }

    /// <summary>
    /// FTS5 rank-ordered projection used by <c>FtsQueryService.SearchAsync</c>. Deliberately
    /// has no source_id column (see the mapper - it reproduces the raw-id/no-source shape).
    /// </summary>
    internal sealed class FtsProjectSummaryRow
    {
        public long project_id { get; set; }
        public string title { get; set; } = string.Empty;
        public string url { get; set; } = string.Empty;
        public string? client_name { get; set; }
        public int? publish_time_number { get; set; }
        public string? publish_time_text { get; set; }
        public int? proposal_count { get; set; }
        public string? proposal_count_text { get; set; }
        public long? is_unread { get; set; }
        public string enrichment_status { get; set; } = string.Empty;
        public string discovered_at { get; set; } = string.Empty;
        public string? description { get; set; }
        public string? budget { get; set; }
        public int? delivery_days { get; set; }
        public string? project_status { get; set; }
        public string? skills_text { get; set; }
    }

    /// <summary>Single-row COUNT/SUM aggregate from <c>CountTrackedAsync</c>.</summary>
    internal sealed class CountersRow
    {
        public int tracked { get; set; }
        public int unread { get; set; }
    }

    /// <summary>Replicates the old ordinal <c>ReadSummary</c> reader, including defaults.</summary>
    internal static ProjectSummary ToSummary(this ProjectSummaryRow row, SourceRegistry registry)
    {
        var rowSource = SourceId.From(row.source_id);
        return new ProjectSummary
        {
            // The row carries the internal id; the model carries the site-local id.
            ProjectId = registry.ToSiteProjectId(rowSource, row.project_id),
            Source = rowSource,
            Title = row.title,
            Url = row.url,
            ClientName = row.client_name ?? string.Empty,
            PublishTimeNumber = row.publish_time_number ?? 0,
            PublishTimeText = row.publish_time_text ?? string.Empty,
            ProposalCount = row.proposal_count ?? 0,
            ProposalCountText = row.proposal_count_text ?? string.Empty,
            Description = row.description ?? string.Empty,
            Budget = row.budget,
            DeliveryDays = row.delivery_days,
            IsUnread = row.is_unread is not null && row.is_unread != 0,
            EnrichmentStatus = Enum.Parse<EnrichmentStatus>(row.enrichment_status),
            DiscoveredAt = DateTimeOffset.Parse(row.discovered_at),
            ProjectStatus = row.project_status,
            SkillsText = row.skills_text ?? string.Empty,
            EnrichedAt = row.enriched_at is null ? null : DateTimeOffset.Parse(row.enriched_at),
        };
    }

    /// <summary>
    /// Replicates the inline projects-LEFT-JOIN-owners reader of <c>GetDetailsAsync</c> /
    /// <c>GetAllDetailsAsync</c> exactly, including the nested <c>Owner</c> construction
    /// (fresh <c>Owner()</c> when the join is null, internal-id band subtraction for the
    /// owner id, and the un-parsed raw <c>registered_at</c> text).
    /// </summary>
    internal static ProjectDetails ToDetails(this ProjectDetailsRow row, SourceRegistry registry)
    {
        var rowSource = SourceId.From(row.source_id);
        return new ProjectDetails
        {
            Source = rowSource,
            ProjectId = registry.ToSiteProjectId(rowSource, row.project_id),
            Title = row.title,
            Url = row.url,
            Description = row.description ?? string.Empty,
            Budget = row.budget,
            DeliveryDays = row.delivery_days,
            EnrichmentStatus = Enum.Parse<EnrichmentStatus>(row.enrichment_status),
            EnrichedAt = row.enriched_at is null ? null : DateTimeOffset.Parse(row.enriched_at),
            ProjectStatus = row.project_status,
            PublishTimeNumber = row.publish_time_number ?? 0,
            PublishTimeText = row.publish_time_text ?? string.Empty,
            ProposalCount = row.proposal_count ?? 0,
            ProposalCountText = row.proposal_count_text ?? string.Empty,
            DiscoveredAt = row.discovered_at is null ? DateTimeOffset.UtcNow : DateTimeOffset.Parse(row.discovered_at),
            Owner = row.owner_id is null
                ? new Owner()
                : new Owner
                {
                    Source = rowSource,
                    OwnerId = registry.ToSiteProjectId(rowSource, row.owner_id.Value),
                    Name = row.name ?? string.Empty,
                    ProfileUrl = row.profile_url,
                    AvatarUrl = row.avatar_url,
                    Rating = row.rating,
                    CompletedProjectsCount = row.completed_projects_count,
                    HiringRatePercent = row.hiring_rate_percent,
                    RegisteredAt = row.registered_at,
                    OpenProjectsCount = row.open_projects_count,
                    InProgressProjectsCount = row.in_progress_projects_count,
                    OngoingCommunicationsCount = row.ongoing_communications_count,
                },
        };
    }

    /// <summary>Replicates <c>OwnerRepository</c>'s readers, including the owner-id band subtraction.</summary>
    internal static Owner ToOwner(this OwnerRow row, SourceRegistry registry)
    {
        var rowSource = SourceId.From(row.source_id);
        return new Owner
        {
            Source = rowSource,
            OwnerId = registry.ToSiteProjectId(rowSource, row.owner_id),
            Name = row.name,
            ProfileUrl = row.profile_url,
            AvatarUrl = row.avatar_url,
            Rating = row.rating,
            CompletedProjectsCount = row.completed_projects_count,
            HiringRatePercent = row.hiring_rate_percent,
            RegisteredAt = row.registered_at,
            OpenProjectsCount = row.open_projects_count,
            InProgressProjectsCount = row.in_progress_projects_count,
            OngoingCommunicationsCount = row.ongoing_communications_count,
        };
    }

    /// <summary>Replicates the old ordinal <c>ReadAsset</c> reader, including defaults.</summary>
    internal static Asset ToAsset(this AssetRow row) => new()
    {
        AssetId = row.asset_id,
        // Internal row id of the owning project (child tables key on it).
        ProjectId = row.project_id,
        FileName = row.file_name,
        Url = row.url ?? string.Empty,
        RawUrl = row.raw_url,
        LocalPath = row.local_path,
        SizeBytes = row.size_bytes,
        Extension = row.extension,
        RequiresAuth = row.requires_auth is not null && row.requires_auth != 0,
        SizeText = row.size_text,
    };

    /// <summary>
    /// Replicates <c>FtsQueryService</c>'s reader verbatim - including its historical shape
    /// of storing the raw internal row id as <c>ProjectId</c> and never setting
    /// <c>Source</c> (defaults to <see cref="SourceId.Default"/>). Preserved as-is; not a
    /// place for this refactor to change observable behavior.
    /// </summary>
    internal static ProjectSummary ToFtsSummary(this FtsProjectSummaryRow row) => new()
    {
        ProjectId = row.project_id,
        Title = row.title,
        Url = row.url,
        ClientName = row.client_name ?? string.Empty,
        PublishTimeNumber = row.publish_time_number ?? 0,
        PublishTimeText = row.publish_time_text ?? string.Empty,
        ProposalCount = row.proposal_count ?? 0,
        ProposalCountText = row.proposal_count_text ?? string.Empty,
        IsUnread = row.is_unread is not null && row.is_unread != 0,
        EnrichmentStatus = Enum.Parse<EnrichmentStatus>(row.enrichment_status),
        DiscoveredAt = DateTimeOffset.Parse(row.discovered_at),
        Description = row.description ?? string.Empty,
        Budget = row.budget,
        DeliveryDays = row.delivery_days,
        ProjectStatus = row.project_status,
        SkillsText = row.skills_text ?? string.Empty,
    };
}
