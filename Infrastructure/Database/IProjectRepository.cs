using MostaqlK.Core;
using MostaqlK.Models;

namespace MostaqlK.Infrastructure.Database;

/// <summary>
/// Persists and queries projects (summary + enriched details) in the local SQLite store.
/// </summary>
public interface IProjectRepository
{
    Task<Result<bool>> InsertSummaryAsync(ProjectSummary project, CancellationToken cancellationToken = default);

    Task<Result<bool>> UpsertDetailsAsync(ProjectDetails details, CancellationToken cancellationToken = default);

    Task<Result<IReadOnlySet<long>>> GetAllKnownProjectIdsAsync(CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<ProjectSummary>>> GetRecentAsync(int limit, CancellationToken cancellationToken = default);

    Task<Result<long?>> GetNewestProjectIdAsync(CancellationToken cancellationToken = default);

    Task<Result<ProjectDetails?>> GetDetailsAsync(long projectId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Counts projects whose `discovered_at` falls on today's date (UTC). Backs the
    /// "مشاريع مضافة اليوم" stat card in <c>SettingsPanel</c>.
    /// </summary>
    Task<Result<int>> CountAddedTodayAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Total number of tracked projects and, of those, how many are still unread. Backs the status
    /// bar's "N مشروع متتبَّع • N غير مقروء" pair, which counts the whole store rather than
    /// just the page of rows currently loaded into the feed.
    /// </summary>
    Task<Result<(int Tracked, int Unread)>> CountTrackedAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes every project row (and its skills, assets and search-index entries). Only used by
    /// the design-parity seeder (<c>--seed-design-data</c>) to keep seeding idempotent — the
    /// pipeline itself never deletes, per the no-update policy.
    /// </summary>
    Task<Result<bool>> ClearAllAsync(CancellationToken cancellationToken = default);
}
