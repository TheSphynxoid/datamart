using DataMart.Domain.Entities;

namespace DataMart.Application.Common.Interfaces;

public interface IAccessLogService
{
    Task LogAsync(string userId, AccessAction action, int? mappingId = null, int? batchId = null, string? correlationId = null, CancellationToken cancellationToken = default);
}