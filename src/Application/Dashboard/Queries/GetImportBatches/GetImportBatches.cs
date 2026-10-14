using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataMart.Application.Common.Interfaces;
using DataMart.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DataMart.Application.Dashboard.Queries.GetImportBatches;

public record ImportBatchListItemDto(
    int Id,
    string FileName,
    string AffectedTable,
    ImportStatus Status,
    int TotalRows,
    int ProcessedRows,
    int SkippedRows,
    int JournalRowsWritten,
    bool JournalIncomplete,
    string? FailedReason,
    string? ExecutedBy,
    DateTimeOffset Created);

public record ImportBatchesPage(int Page, int PageSize, int TotalCount, IReadOnlyList<ImportBatchListItemDto> Items);

public record GetImportBatchesQuery(
    int Page = 1,
    int PageSize = 20,
    ImportStatus? Status = null,
    string? AffectedTable = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    string? Search = null) : IRequest<ImportBatchesPage>;

public class GetImportBatchesQueryHandler : IRequestHandler<GetImportBatchesQuery, ImportBatchesPage>
{
    private const int MaxPageSize = 200;

    private readonly IApplicationDbContext _context;

    public GetImportBatchesQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<ImportBatchesPage> Handle(GetImportBatchesQuery request, CancellationToken cancellationToken)
    {
        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, MaxPageSize);

        IQueryable<Domain.Entities.ImportBatch> q = _context.ImportBatches;

        if (request.Status.HasValue)
            q = q.Where(b => b.Status == request.Status.Value);

        if (!string.IsNullOrWhiteSpace(request.AffectedTable))
            q = q.Where(b => b.AffectedTable == request.AffectedTable);

        if (request.From.HasValue)
            q = q.Where(b => b.Created >= request.From.Value);

        if (request.To.HasValue)
            q = q.Where(b => b.Created <= request.To.Value);

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            // Free-text search across file name and the user who executed the
            // import. Case-insensitive via LOWER() on both sides — provider-neutral,
            // works on any EF Core provider (Postgres, SqlServer, etc.).
            var needle = request.Search.Trim().ToLower();
            q = q.Where(b =>
                b.FileName.ToLower().Contains(needle) ||
                (b.ExecutedBy != null && b.ExecutedBy.ToLower().Contains(needle)));
        }

        var totalCount = await q.CountAsync(cancellationToken);

        var items = await q
            .OrderByDescending(b => b.Created)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(b => new ImportBatchListItemDto(
                b.Id,
                b.FileName,
                b.AffectedTable,
                b.Status,
                b.TotalRows,
                b.ProcessedRows,
                b.SkippedRows,
                b.JournalRowsWritten,
                b.JournalIncomplete,
                b.FailedReason,
                b.ExecutedBy,
                b.Created))
            .ToListAsync(cancellationToken);

        return new ImportBatchesPage(page, pageSize, totalCount, items);
    }
}
