using System.Threading.Channels;

namespace Burkard.Invoicing.Api.Jobs;

public sealed class JobQueue
{
    private readonly Channel<long> _channel = Channel.CreateUnbounded<long>(
        new UnboundedChannelOptions { SingleReader = true });

    public void Enqueue(long jobId) => _channel.Writer.TryWrite(jobId);

    public IAsyncEnumerable<long> ReadAllAsync(CancellationToken ct) =>
        _channel.Reader.ReadAllAsync(ct);
}
