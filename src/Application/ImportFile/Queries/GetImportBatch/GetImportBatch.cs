using DataMart.Application.Common.Interfaces;
using DataMart.Domain.Enums;

namespace DataMart.Application.ImportFile.Queries.GetImportBatch;

public record class ImportBatchDto(
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
    string? FailedReason);

public record class GetImportBatchQuery(int Id) : IRequest<ImportBatchDto>;

public class GetImportBatchQueryHandler : IRequestHandler<GetImportBatchQuery, ImportBatchDto>
{
    private readonly IApplicationDbContext _context;

    public GetImportBatchQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<ImportBatchDto> Handle(GetImportBatchQuery request, CancellationToken cancellationToken)
    {
        var batch = await _context.ImportBatches.FindAsync([request.Id], cancellationToken);
        Guard.Against.NotFound(request.Id, batch);

        return new ImportBatchDto(
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
            batch.FailedReason);
    }
}