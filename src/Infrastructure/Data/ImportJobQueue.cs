using System.Threading.Channels;
using DataMart.Application.Common.Interfaces;

namespace DataMart.Infrastructure.Data;

/// <summary>
/// In-process FIFO queue of import jobs. Jobs are processed one at a time by
/// <see cref="ImportProcessingService"/> — concurrent bulk imports would fight
/// over the COPY transaction and make progress reporting meaningless.
/// </summary>
public sealed class ImportJobQueue : IImportJobQueue
{
    private readonly Channel<ImportJob> _channel = Channel.CreateUnbounded<ImportJob>();

    public ValueTask EnqueueAsync(ImportJob job, CancellationToken ct) =>
        _channel.Writer.WriteAsync(job, ct);

    public async ValueTask<ImportJob?> DequeueAsync(CancellationToken ct)
    {
        if (await _channel.Reader.WaitToReadAsync(ct).ConfigureAwait(false))
            return _channel.Reader.TryRead(out var job) ? job : null;
        return null; // channel completed (writer closed) — no more jobs
    }
}