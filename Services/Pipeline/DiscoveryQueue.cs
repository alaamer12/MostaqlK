using System.Threading.Channels;
using MostaqlK.Core.Sources;

namespace MostaqlK.Services.Pipeline;

/// <summary>
/// FIFO queue of newly discovered projects awaiting enrichment, backed by
/// <see cref="Channel{T}"/> so producers (the poll service) and consumers
/// (the worker pool) can run concurrently without extra locking.
/// One instance per source, so items are addressed by <see cref="SourceProjectId"/>
/// — the composite is what keeps two sources' numeric ids from aliasing each other.
/// </summary>
public sealed class DiscoveryQueue
{
    private readonly Channel<SourceProjectId> _channel = Channel.CreateUnbounded<SourceProjectId>(
        new UnboundedChannelOptions { SingleReader = false, SingleWriter = false });

    /// <summary>
    /// Approximate number of projects currently queued awaiting enrichment. Used by
    /// <see cref="MostaqlK.UI.TrayIcon.TrayIconService"/> to detect a draining backlog.
    /// </summary>
    public int Count => _channel.Reader.Count;

    public ValueTask EnqueueAsync(SourceProjectId projectId, CancellationToken cancellationToken = default) =>
        _channel.Writer.WriteAsync(projectId, cancellationToken);

    public IAsyncEnumerable<SourceProjectId> ReadAllAsync(CancellationToken cancellationToken = default) =>
        _channel.Reader.ReadAllAsync(cancellationToken);

    public void Complete() => _channel.Writer.TryComplete();
}
