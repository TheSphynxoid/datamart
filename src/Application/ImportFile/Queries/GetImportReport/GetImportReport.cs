using DataMart.Application.Common.Interfaces;
using DataMart.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace DataMart.Application.ImportFile.Queries.GetImportReport;

public record class ImportReportError(int RowNumber, string Reason);

public record class ImportReportDto(
    int? BatchId,
    ImportStatus Status,
    int TotalRows,
    int ImportedRows,
    int RejectedRows,
    List<ImportReportError> Errors);

public record class GetImportReportQuery(int Id) : IRequest<ImportReportDto>;

public class GetImportReportQueryHandler : IRequestHandler<GetImportReportQuery, ImportReportDto>
{
    private readonly IApplicationDbContext _context;

    public GetImportReportQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<ImportReportDto> Handle(GetImportReportQuery request, CancellationToken cancellationToken)
    {
        var batch = await _context.ImportBatches.FindAsync([request.Id], cancellationToken);
        Guard.Against.NotFound(request.Id, batch);

        var records = await _context.ImportRecords
            .Where(r => r.ImportBatchId == request.Id)
            .ToListAsync(cancellationToken);

        var importedCount = records.Count(r => r.Status == ImportRowStatus.Imported);
        var rejectedRecords = records.Where(r => r.Status == ImportRowStatus.Rejected).ToList();
        var errors = rejectedRecords
            .Select(r => new ImportReportError(
                int.TryParse(r.SourceKey, out var n) ? n : 0,
                r.ErrorMessage ?? string.Empty))
            .OrderBy(e => e.RowNumber)
            .ToList();

        // The report's rapprot numbers come from the row-level journal; fall back to the
        // batch counters if no journal exists yet (e.g. import from before journaling).
        var importedRows = importedCount > 0 ? importedCount : batch.TotalRows - batch.SkippedRows;

        return new ImportReportDto(
            batch.Id,
            batch.Status,
            batch.TotalRows,
            importedRows,
            rejectedRecords.Count,
            errors);
    }
}
