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

namespace DataMart.Application.Dashboard.Queries.GetImportJournal;

public record ImportJournalEntryDto(
    int Id,
    string SourceKey,
    ImportRowStatus Status,
    string? ErrorMessage,
    string? Payload,
    DateTimeOffset Created);

public record ImportJournalPage(int BatchId, int Page, int PageSize, int TotalCount, IReadOnlyList<ImportJournalEntryDto> Items);

public record GetImportJournalQuery(
    int BatchId,
    int Page = 1,
    int PageSize = 50) : IRequest<ImportJournalPage>;

public class GetImportJournalQueryHandler : IRequestHandler<GetImportJournalQuery, ImportJournalPage>
{
    private const int MaxPageSize = 500;

    private readonly IApplicationDbContext _context;

    public GetImportJournalQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<ImportJournalPage> Handle(GetImportJournalQuery request, CancellationToken cancellationToken)
    {
        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, MaxPageSize);

        // A non-existent batch id should be a 404, not an empty page. Guard at the
        // boundary so the caller (web endpoint) can rely on a clean result.
        var batchExists = await _context.ImportBatches
            .AnyAsync(b => b.Id == request.BatchId, cancellationToken);
        Guard.Against.NotFound(request.BatchId, batchExists);

        var q = _context.ImportRecords
            .Where(r => r.ImportBatchId == request.BatchId);

        var totalCount = await q.CountAsync(cancellationToken);

        var items = await q
            .OrderBy(r => r.Created)
            .ThenBy(r => r.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(r => new ImportJournalEntryDto(
                r.Id,
                r.SourceKey ?? string.Empty,
                r.Status,
                r.ErrorMessage,
                r.Payload,
                r.Created))
            .ToListAsync(cancellationToken);

        return new ImportJournalPage(request.BatchId, page, pageSize, totalCount, items);
    }
}
