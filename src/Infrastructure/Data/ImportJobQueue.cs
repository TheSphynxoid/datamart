using System.Threading.Channels;
using DataMart.Application.Common.Interfaces;

namespace DataMart.Infrastructure.Data;

/// <summary>
/// In-process FIFO queue of import jobs. The <see cref="ImportProcessingService"/>
/// dispatches jobs to a <see cref="TableTaskScheduler"/>: imports to different
/// tables run concurrently (bounded by <c>Imports:MaxConcurrent</c>); imports to
/// the same table run strictly in this queue's FIFO order.
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