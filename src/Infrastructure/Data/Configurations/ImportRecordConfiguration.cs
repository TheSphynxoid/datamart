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
        builder.Property(t => t.ErrorMessage).HasMaxLength(2000);
        builder.Property(t => t.Status).HasConversion<int>();
    }
}
