using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using DataMart.Application.Common.Interfaces;
using DataMart.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DataMart.Application.Dashboard.Queries.GetImportJournalSummary;

/// <summary>One box in the grouped journal drill-down: every rejected row with
/// the same <see cref="ImportRecord.ErrorCode"/> ("same issue = same box").
/// <paramref name="Message"/> is a representative row's full detail (first by
/// row-number order) — the expanded box shows every row's own message.
/// Rows are reachable through the paged journal query filtered by this code.</summary>
public record RejectionGroupDto(
    string ErrorCode,
    string Message,
    int Count,
    int? MinRowNumber,
    int? MaxRowNumber);

/// <summary>Grouped tier-2 journal (drill-down summary): imported rows collapse
/// into a single count (row-by-row entries would be identical); rejections are
/// grouped by stable error code so the same issue shows in the same box.</summary>
public record ImportJournalSummaryDto(
    int BatchId,
    int TotalRows,
    int ImportedRows,
    int RejectedRows,
    IReadOnlyList<RejectionGroupDto> RejectionGroups)
{
    /// <summary>Group key for rows predating the error-code scheme (no code recorded).</summary>
    public const string UncodedGroupKey = "uncoded";
}

public record GetImportJournalSummaryQuery(int BatchId) : IRequest<ImportJournalSummaryDto>;

public class GetImportJournalSummaryQueryHandler : IRequestHandler<GetImportJournalSummaryQuery, ImportJournalSummaryDto>
{
    /// <summary>Distinct issue codes are bounded by the validation logic (a handful
    /// of rules/parse modes per column); the cap only guards pathological data.
    /// Aggregation happens in SQL — no journal rows are materialised.</summary>
    private const int MaxGroups = 100;

    private readonly IApplicationDbContext _context;

    public GetImportJournalSummaryQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<ImportJournalSummaryDto> Handle(GetImportJournalSummaryQuery request, CancellationToken cancellationToken)
    {
        var batch = await _context.ImportBatches.FindAsync([request.BatchId], cancellationToken);
        Guard.Against.NotFound(request.BatchId, batch);

        var q = _context.ImportRecords
            .Where(r => r.ImportBatchId == request.BatchId);

        var imported = await q
            .Where(r => r.Status == ImportRowStatus.Imported)
            .CountAsync(cancellationToken);

        var rejected = await q
            .Where(r => r.Status == ImportRowStatus.Rejected)
            .CountAsync(cancellationToken);

        var groups = await q
            .Where(r => r.Status == ImportRowStatus.Rejected)
            .GroupBy(r => r.ErrorCode ?? ImportJournalSummaryDto.UncodedGroupKey)
            .OrderByDescending(g => g.Count())
            .Take(MaxGroups)
            .Select(g => new RejectionGroupDto(
                g.Key,
                // Representative detail for the collapsed box: a stable, deterministic
                // pick (no rows are materialised just to label the group).
                g.Min(r => r.ErrorMessage) ?? g.Key,
                g.Count(),
                g.Min(r => r.RowNumber),
                g.Max(r => r.RowNumber)))
            .ToListAsync(cancellationToken);

        return new ImportJournalSummaryDto(
            request.BatchId,
            imported + rejected,
            imported,
            rejected,
            groups);
    }
}
