using MostaqlK.Core;
using MostaqlK.Core.Sources;
using MostaqlK.Models;

namespace MostaqlK.Infrastructure.Database;

/// <summary>
/// The v4 profile-crawl snapshot store (R2.8). Append-only: every crawl inserts its own row and
/// nothing else. The profile projection is rebuilt only from <c>ok = 1</c> rows, so a failed
/// crawl can never blank, half-update, or poison a good profile — across sources or over time.
/// </summary>
public interface IProfileSnapshotRepository
{
    /// <summary>Insert one crawl attempt. Never touches an existing row (append-only).</summary>
    public Task<Result<long>> InsertAsync(ProfileSnapshot snapshot, CancellationToken cancellationToken = default);

    /// <summary>The newest usable (<c>ok = 1</c>) snapshot for one source, or null. Cross-source reads are impossible by construction.</summary>
    public Task<Result<ProfileSnapshot?>> GetLatestOkAsync(SourceId source, CancellationToken cancellationToken = default);

    /// <summary>Trim to the newest <paramref name="keep"/> rows for ONE source only; other sources are never touched.</summary>
    public Task<Result<int>> PruneToNewestAsync(SourceId source, int keep, CancellationToken cancellationToken = default);
}
