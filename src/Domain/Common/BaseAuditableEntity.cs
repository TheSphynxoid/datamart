using DataMart.Application.Common.Interfaces;

namespace DataMart.Domain.Common;

public abstract class BaseAuditableEntity : BaseEntity, IImportable
{
    [Exclude]
    public DateTimeOffset Created { get; set; }
    [Exclude]
    public string? CreatedBy { get; set; }
    [Exclude]
    public DateTimeOffset LastModified { get; set; }
    [Exclude]
    public string? LastModifiedBy { get; set; }
    [Exclude]
    public int? ImportBatchId { get; set; }
    [Exclude]
    public string? SourceKey { get; set; }
}
