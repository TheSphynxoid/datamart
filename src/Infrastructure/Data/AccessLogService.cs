using DataMart.Application.Common.Interfaces;
using DataMart.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace DataMart.Infrastructure.Data;

public class AccessLogService : IAccessLogService
{
    private readonly IApplicationDbContext _context;

    public AccessLogService(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task LogAsync(string userId, AccessAction action, int? mappingId = null, int? batchId = null, string? correlationId = null, CancellationToken cancellationToken = default)
    {
        var log = new AccessLog
        {
            UserId = userId,
            Action = action,
            MappingId = mappingId,
            BatchId = batchId,
            Timestamp = DateTimeOffset.UtcNow,
            CorrelationId = correlationId
        };

        _context.AccessLogs.Add(log);
        await _context.SaveChangesAsync(cancellationToken);
    }
}