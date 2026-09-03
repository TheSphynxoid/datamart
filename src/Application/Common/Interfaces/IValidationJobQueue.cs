using DataMart.Domain.Enums;

namespace DataMart.Application.Common.Interfaces;

/// <summary>
/// A queued dry-run validation. Mirrors the import-side
/// <see cref="ImportJob"/> but with no batch audit record — validation does
/// not write to the database, so there is no entity to track it.
/// </summary>
public sealed record ValidationJob(
    Guid ValidationId,
    string TempPath,
    int MappingId,
    EmptyFieldMode EmptyMode,
    int? SheetIndex = null);

/// <summary>
/// In-process FIFO queue of validation jobs. Separate from
/// <see cref="IImportJobQueue"/> so a long-running validation never
/// blocks a real import (or vice versa). One worker
/// (<see cref="ValidationProcessingService"/>) drains this channel.
/// </summary>
public interface IValidationJobQueue
{
    ValueTask EnqueueAsync(ValidationJob job, CancellationToken ct);

    ValueTask<ValidationJob?> DequeueAsync(CancellationToken ct);
}
