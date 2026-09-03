using DataMart.Domain.Common;

namespace DataMart.Domain.Entities;

[Exclude]
public class AccessLog : BaseEntity
{
    public required string UserId { get; init; }
    public int? MappingId { get; init; }
    public int? BatchId { get; init; }
    public required AccessAction Action { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
    public string? CorrelationId { get; init; }
}

public enum AccessAction
{
    ViewSchema,
    CreateMapping,
    UpdateMapping,
    ExecuteImport,
    PreviewData
}