using System;
using System.Collections.Generic;
using System.Text;

namespace DataMart.Domain.Entities;

public class ImportRecord : BaseAuditableEntity
{
    public string? Reference { get; set; }
    public string? Description { get; set; }
    public decimal Amount { get; set; }
    public DateTimeOffset RecordDate { get; set; }
}
