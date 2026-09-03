using DataMart.Domain.Enums;

namespace DataMart.Application.Common.Interfaces;

/// <summary>
/// A queued background import job. The request handler enqueues it after validation;
/// the <see cref="IImportJobQueue"/> consumer (hosted service) executes it.
/// </summary>
    public sealed record ImportJob(string TempPath, int BatchId, int MappingId, EmptyFieldMode EmptyMode, int? SheetIndex = null);

public interface IImportJobQueue
{
    ValueTask EnqueueAsync(ImportJob job, CancellationToken ct);
    ValueTask<ImportJob?> DequeueAsync(CancellationToken ct);
}
