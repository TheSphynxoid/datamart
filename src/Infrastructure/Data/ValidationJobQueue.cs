using System.Threading.Channels;
using DataMart.Application.Common.Interfaces;

namespace DataMart.Infrastructure.Data;

/// <summary>
/// In-process FIFO channel of <see cref="ValidationJob"/>s. Mirrors
/// <see cref="ImportJobQueue"/> but lives on its own channel so a long
/// dry-run does not block a real import.
/// </summary>
public sealed class ValidationJobQueue : IValidationJobQueue
{
    private readonly Channel<ValidationJob> _channel = Channel.CreateUnbounded<ValidationJob>();

    public ValueTask EnqueueAsync(ValidationJob job, CancellationToken ct) =>
        _channel.Writer.WriteAsync(job, ct);

    public async ValueTask<ValidationJob?> DequeueAsync(CancellationToken ct)
    {
        if (await _channel.Reader.WaitToReadAsync(ct).ConfigureAwait(false))
            return _channel.Reader.TryRead(out var job) ? job : null;
        return null;
    }
}
