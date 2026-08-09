using System.Threading.Channels;

namespace MostaqlK.Services.Pipeline;

/// <summary>
/// FIFO queue of newly discovered project IDs awaiting enrichment, backed by
/// <see cref="Channel{T}"/> so producers (the poll service) and consumers
/// (the worker pool) can run concurrently without extra locking.
/// </summary>
public sealed class DiscoveryQueue
{
    private readonly Channel<long> _channel = Channel.CreateUnbounded<long>(
        new UnboundedChannelOptions { SingleReader = false, SingleWriter = false });

    /// <summary>
    /// Approximate number of project IDs currently queued awaiting enrichment. Used by
    /// <see cref="MostaqlK.UI.TrayIcon.TrayIconService"/> to detect a draining backlog.
    /// </summary>
    public int Count => _channel.Reader.Count;

    public ValueTask EnqueueAsync(long projectId, CancellationToken cancellationToken = default) =>
        _channel.Writer.WriteAsync(projectId, cancellationToken);

    public IAsyncEnumerable<long> ReadAllAsync(CancellationToken cancellationToken = default) =>
        _channel.Reader.ReadAllAsync(cancellationToken);

    public void Complete() => _channel.Writer.TryComplete();
}
