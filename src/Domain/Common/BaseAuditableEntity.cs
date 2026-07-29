using DataMart.Application.Common.Interfaces;

namespace DataMart.Domain.Common;

public abstract class BaseAuditableEntity : BaseEntity, IImportable
{
    public DateTimeOffset Created { get; set; }

    public string? CreatedBy { get; set; }

    public DateTimeOffset LastModified { get; set; }

    public string? LastModifiedBy { get; set; }
}
