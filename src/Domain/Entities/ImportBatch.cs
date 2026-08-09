using System;
using System.Collections.Generic;
using System.Text;

namespace DataMart.Domain.Entities;

[Exclude]
public class ImportBatch : BaseAuditableEntity
{
    public required string FileName { get; set; }
    
    public required string AffectedTable { get; set; }

    //public required string AppliedConfiguration; //< Make configurations parsable from strings and convertable to strings.
}
