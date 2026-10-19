using DataMart.Domain.Enums;

namespace DataMart.Application.Common.Interfaces;

/// <summary>
/// A queued background import job. The request handler enqueues it after validation;
/// the <see cref="IImportJobQueue"/> consumer (hosted service) executes it.
/// <para><see cref="TargetTable"/> is the table the import writes (the mapping's
/// target entity at enqueue time): the processing service serializes imports per
/// table but runs imports to different tables concurrently (bounded by a
/// process-wide cap). Same-table ordering is strict FIFO by enqueue order.</para>
/// </summary>
    public sealed record ImportJob(string TempPath, int BatchId, int MappingId, EmptyFieldMode EmptyMode, string TargetTable, int? SheetIndex = null);

public interface IImportJobQueue
{
    ValueTask EnqueueAsync(ImportJob job, CancellationToken ct);
    ValueTask<ImportJob?> DequeueAsync(CancellationToken ct);
}
