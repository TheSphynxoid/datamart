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

        // Load only what the report shows: two counts plus the rejected rows'
        // identity/message. Materialising every journal row (with its full
        // Payload JSON, tracked by the change tracker) just to count them is a
        // memory bomb on exactly the file sizes this tool is built for.
        var importedCount = await _context.ImportRecords
            .Where(r => r.ImportBatchId == request.Id && r.Status == ImportRowStatus.Imported)
            .CountAsync(cancellationToken);

        var errors = await _context.ImportRecords
            .Where(r => r.ImportBatchId == request.Id && r.Status == ImportRowStatus.Rejected)
            .OrderBy(r => r.SourceKey)
            .Select(r => new { r.SourceKey, r.ErrorMessage })
            .ToListAsync(cancellationToken);

        var reportErrors = errors
            .Select(r => new ImportReportError(
                int.TryParse(r.SourceKey, out var n) ? n : 0,
                r.ErrorMessage ?? string.Empty))
            .ToList();

        // The report's numbers come from the row-level journal; fall back to the
        // batch counters if no journal exists yet (e.g. import from before journaling).
        var importedRows = importedCount > 0 ? importedCount : batch.TotalRows - batch.SkippedRows;

        return new ImportReportDto(
            batch.Id,
            batch.Status,
            batch.TotalRows,
            importedRows,
            reportErrors.Count,
            reportErrors);
    }
}
