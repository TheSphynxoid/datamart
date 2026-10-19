using System;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using DataMart.Application.Common.Interfaces;
using DataMart.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DataMart.Application.Dashboard.Queries.GetBatchDetail;

/// <summary>
/// Batch-detail query (journal framework spec §6): the tier-1 record including
/// the import-time snapshot payloads. Snapshots are viewable/downloadable for
/// audit — read-only evidence of the exact mapping and rule set the import ran
/// on. Deliberately separate from the lean <c>GetImportBatch</c> poll DTO: the
/// snapshot payloads can each approach the 1 MB cap and must not ride a 500 ms
/// status poll.
/// </summary>
public record BatchDetailDto(
    int Id,
    string FileName,
    string AffectedTable,
    ImportStatus Status,
    int TotalRows,
    int ProcessedRows,
    int SkippedRows,
    int JournalRowsWritten,
    bool JournalIncomplete,
    string? SkippedRowsPreview,
    string? FailedReason,
    string? ExecutedBy,
    DateTimeOffset Created,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    int? MinImpactedId,
    int? MaxImpactedId,
    string? ControlTotalsJson,
    string? MappingSnapshotJson,
    string? RulesSnapshotJson,
    string? JournalExtrasJson);

public record GetBatchDetailQuery(int Id) : IRequest<BatchDetailDto>;

public class GetBatchDetailQueryHandler : IRequestHandler<GetBatchDetailQuery, BatchDetailDto>
{
    private readonly IApplicationDbContext _context;

    public GetBatchDetailQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<BatchDetailDto> Handle(GetBatchDetailQuery request, CancellationToken cancellationToken)
    {
        var batch = await _context.ImportBatches
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == request.Id, cancellationToken);
        Guard.Against.NotFound(request.Id, batch);

        return new BatchDetailDto(
            batch.Id,
            batch.FileName,
            batch.AffectedTable,
            batch.Status,
            batch.TotalRows,
            batch.ProcessedRows,
            batch.SkippedRows,
            batch.JournalRowsWritten,
            batch.JournalIncomplete,
            batch.SkippedRowsPreview,
            batch.FailedReason,
            batch.ExecutedBy,
            batch.Created,
            batch.StartedAt,
            batch.CompletedAt,
            batch.MinImpactedId,
            batch.MaxImpactedId,
            batch.ControlTotalsJson,
            batch.MappingSnapshotJson,
            batch.RulesSnapshotJson,
            batch.JournalExtrasJson);
    }
}
