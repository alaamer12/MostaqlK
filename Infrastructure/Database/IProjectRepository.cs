using MostaqlK.Core;
using MostaqlK.Models;

namespace MostaqlK.Infrastructure.Database;

/// <summary>
/// Persists and queries projects (summary + enriched details) in the local SQLite store.
/// </summary>
public interface IProjectRepository
{
    public Task<Result<bool>> InsertSummaryAsync(ProjectSummary project, CancellationToken cancellationToken = default);

    public Task<Result<bool>> UpsertDetailsAsync(ProjectDetails details, CancellationToken cancellationToken = default);

    public Task<Result<IReadOnlySet<long>>> GetAllKnownProjectIdsAsync(CancellationToken cancellationToken = default);

    public Task<Result<IReadOnlyList<ProjectSummary>>> GetRecentAsync(int limit, CancellationToken cancellationToken = default);

    public Task<Result<long?>> GetNewestProjectIdAsync(CancellationToken cancellationToken = default);

    public Task<Result<ProjectDetails?>> GetDetailsAsync(long projectId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Counts projects whose `discovered_at` falls on today's date (UTC). Backs the
    /// "مشاريع مضافة اليوم" stat card in <c>SettingsPanel</c>.
    /// </summary>
    public Task<Result<int>> CountAddedTodayAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a project ID to the discovery backlog for recovery.
    /// </summary>
    public Task<Result<bool>> AddToBacklogAsync(long projectId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a project ID from the discovery backlog (usually after successful enrichment).
    /// </summary>
    public Task<Result<bool>> RemoveFromBacklogAsync(long projectId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches all project IDs currently in the discovery backlog.
    /// </summary>
    public Task<Result<IReadOnlyList<long>>> GetBacklogIdsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes backlog items older than the specified number of days.
    /// </summary>
    public Task<Result<int>> CleanOldBacklogAsync(int days = 30, CancellationToken cancellationToken = default);

    /// <summary>
    /// Total number of tracked projects and, of those, how many are still unread. Backs the status
    /// bar's "N مشروع متتبَّع • N غير مقروء" pair, which counts the whole store rather than
    /// just the page of rows currently loaded into the feed.
    /// </summary>
    public Task<Result<(int Tracked, int Unread)>> CountTrackedAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists <c>is_unread = 0</c> for a single project. Called when a card is opened/selected
    /// so the read state survives past the next <c>LoadAsync</c>/pipeline-triggered reload instead
    /// of only living on the transient <c>ProjectCardViewModel</c> instance.
    /// </summary>
    public Task<Result<bool>> MarkAsReadAsync(long projectId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists <c>is_unread = 0</c> for every project row. Backs the feed footer's
    /// "تحديد الكل كمقروء" action — without this, the DB's `SUM(is_unread)` used by
    /// <see cref="CountTrackedAsync"/> would resurrect the old unread rows on the very next
    /// reload (e.g. triggered by a newly discovered project), making the badge jump back up.
    /// </summary>
    public Task<Result<bool>> MarkAllAsReadAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes every project row (and its skills, assets and search-index entries). Only used by
    /// the design-parity seeder (<c>--seed-design-data</c>) to keep seeding idempotent — the
    /// pipeline itself never deletes, per the no-update policy.
    /// </summary>
    public Task<Result<bool>> ClearAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes only the project rows (and their skills/assets/search-index entries) whose
    /// <c>project_id</c> falls within <paramref name="minProjectId"/>..<paramref name="maxProjectId"/>
    /// (inclusive). Used by <c>DesignDataSeeder.PurgeSeededRowsAsync</c> to strip leftover seed
    /// rows out of an otherwise-live store without touching real scraped rows outside the range.
    /// </summary>
    public Task<Result<int>> DeleteByProjectIdRangeAsync(long minProjectId, long maxProjectId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches all enriched project details for debugging/export.
    /// </summary>
    public Task<Result<IReadOnlyList<ProjectDetails>>> GetAllDetailsAsync(CancellationToken cancellationToken = default);
}
