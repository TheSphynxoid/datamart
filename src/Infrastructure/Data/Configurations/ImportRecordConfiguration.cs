using System;
using System.Collections.Generic;
using System.Text;
using DataMart.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DataMart.Infrastructure.Data.Configurations;

public class ImportRecordConfiguration : IEntityTypeConfiguration<ImportRecord>
{
    public void Configure(EntityTypeBuilder<ImportRecord> builder)
    {
        // Payload holds the JSON snapshot of a row's mapped values; no length cap needed
        // (rows are bounded), keep it as text.
        builder.Property(t => t.ErrorMessage).HasMaxLength(ImportRecord.MaxErrorMessageLength);
        builder.Property(t => t.ErrorCode).HasMaxLength(ImportRecord.MaxErrorCodeLength);
        builder.Property(t => t.Status).HasConversion<int>();

        // Journal framework (spec §7): the drill-down's stable sort/filter —
        // every page of the journal reads the batch's rows in source order.
        builder.HasIndex(r => new { r.ImportBatchId, r.RowNumber });
    }
}
