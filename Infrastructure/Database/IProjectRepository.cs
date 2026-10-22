using MostaqlK.Core;
using MostaqlK.Core.Sources;
using MostaqlK.Models;

namespace MostaqlK.Infrastructure.Database;

/// <summary>
/// Persists and queries projects (summary + enriched details) in the local SQLite store.
/// All identity is composite (<see cref="SourceProjectId"/>/<see cref="SourceId"/>): the same
/// numeric site id may exist on several sources, so no method takes a bare long as a key.
/// </summary>
public interface IProjectRepository
{
    public Task<Result<bool>> InsertSummaryAsync(ProjectSummary project, CancellationToken cancellationToken = default);

    public Task<Result<bool>> UpsertDetailsAsync(ProjectDetails details, CancellationToken cancellationToken = default);

    public Task<Result<IReadOnlySet<SourceProjectId>>> GetAllKnownProjectIdsAsync(CancellationToken cancellationToken = default);

    public Task<Result<IReadOnlyList<ProjectSummary>>> GetRecentAsync(SourceId source, int limit, CancellationToken cancellationToken = default);

    public Task<Result<SourceProjectId?>> GetNewestProjectIdAsync(SourceId source, CancellationToken cancellationToken = default);

    public Task<Result<ProjectDetails?>> GetDetailsAsync(SourceProjectId projectId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Counts this source's projects whose `discovered_at` falls on today's date (UTC). Backs the
    /// "مشاريع مضافة اليوم" stat card in <c>SettingsPanel</c>.
    /// </summary>
    public Task<Result<int>> CountAddedTodayAsync(SourceId source, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a project to the discovery backlog for recovery.
    /// </summary>
    public Task<Result<bool>> AddToBacklogAsync(SourceProjectId projectId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a project from the discovery backlog (usually after successful enrichment).
    /// </summary>
    public Task<Result<bool>> RemoveFromBacklogAsync(SourceProjectId projectId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches all projects currently in the discovery backlog, source-scoped.
    /// </summary>
    public Task<Result<IReadOnlyList<SourceProjectId>>> GetBacklogIdsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes backlog items older than the specified number of days.
    /// </summary>
    public Task<Result<int>> CleanOldBacklogAsync(int days = 30, CancellationToken cancellationToken = default);

    /// <summary>
    /// Total number of tracked projects for one source and, of those, how many are still unread.
    /// Backs the status bar's "N مشروع متتبَّع • N غير مقروء" pair, which counts the whole store
    /// rather than just the page of rows currently loaded into the feed.
    /// </summary>
    public Task<Result<(int Tracked, int Unread)>> CountTrackedAsync(SourceId source, CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists <c>is_unread = 0</c> for a single project. Called when a card is opened/selected
    /// so the read state survives past the next <c>LoadAsync</c>/pipeline-triggered reload instead
    /// of only living on the transient <c>ProjectCardViewModel</c> instance.
    /// </summary>
    public Task<Result<bool>> MarkAsReadAsync(SourceProjectId projectId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists <c>is_unread = 0</c> for every project of one source. Backs the feed footer's
    /// "تحديد الكل كمقروء" action — without this, the DB's `SUM(is_unread)` used by
    /// <see cref="CountTrackedAsync"/> would resurrect the old unread rows on the very next
    /// reload (e.g. triggered by a newly discovered project), making the badge jump back up.
    /// </summary>
    public Task<Result<bool>> MarkAllAsReadAsync(SourceId source, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes every project row (and its skills, assets and search-index entries). Only used by
    /// the design-parity seeder (<c>--seed-design-data</c>) to keep seeding idempotent — the
    /// pipeline itself never deletes, per the no-update policy.
    /// </summary>
    public Task<Result<bool>> ClearAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes only the project rows (and their skills/assets/search-index entries) for one
    /// source whose <c>site_project_id</c> falls within <paramref name="minSiteProjectId"/>..
    /// <paramref name="maxSiteProjectId"/> (inclusive). Used by
    /// <c>DesignDataSeeder.PurgeSeededRowsAsync</c> to strip leftover seed rows out of an
    /// otherwise-live store without touching real scraped rows outside the range.
    /// </summary>
    public Task<Result<int>> DeleteByProjectIdRangeAsync(SourceId source, long minSiteProjectId, long maxSiteProjectId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches all enriched project details for debugging/export.
    /// </summary>
    public Task<Result<IReadOnlyList<ProjectDetails>>> GetAllDetailsAsync(CancellationToken cancellationToken = default);
}
