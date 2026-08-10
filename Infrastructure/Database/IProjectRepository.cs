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
}
