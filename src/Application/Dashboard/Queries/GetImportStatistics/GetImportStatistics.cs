using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataMart.Application.Common.Interfaces;
using DataMart.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DataMart.Application.Dashboard.Queries.GetImportStatistics;

public record ImportStatisticsDto(
    int TotalBatches,
    int SuccessCount,
    int FailedCount,
    int ProcessingCount,
    int TotalRowsImported);

public record GetImportStatisticsQuery : IRequest<ImportStatisticsDto>;

public class GetImportStatisticsQueryHandler : IRequestHandler<GetImportStatisticsQuery, ImportStatisticsDto>
{
    private readonly IApplicationDbContext _context;

    public GetImportStatisticsQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<ImportStatisticsDto> Handle(GetImportStatisticsQuery request, CancellationToken cancellationToken)
    {
        // Aggregate the ImportBatches table once, in the database. Two scalar
        // queries (status counts + total rows imported) so the work stays small
        // and the EF query compiles to a single GROUP BY per metric.
        var byStatus = await _context.ImportBatches
            .GroupBy(b => b.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var totalRowsImported = await _context.ImportBatches
            .Where(b => b.Status == ImportStatus.Success)
            .SumAsync(b => (long?)b.ProcessedRows - b.SkippedRows ?? 0, cancellationToken);

        int Count(ImportStatus s) => byStatus.FirstOrDefault(x => x.Status == s)?.Count ?? 0;

        return new ImportStatisticsDto(
            TotalBatches:   byStatus.Sum(x => x.Count),
            SuccessCount:   Count(ImportStatus.Success),
            FailedCount:    Count(ImportStatus.Failed),
            ProcessingCount: Count(ImportStatus.Processing),
            TotalRowsImported: (int)Math.Max(0, totalRowsImported));
    }
}
