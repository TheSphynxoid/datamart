using System;
using System.Collections.Generic;
using System.Text;
using DataMart.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DataMart.Infrastructure.Data.Configurations;

public class ImportBatchConfiguration : IEntityTypeConfiguration<ImportBatch>
{
    public void Configure(EntityTypeBuilder<ImportBatch> builder)
    {
        builder.HasMany(b => b.Records)
            .WithOne(r => r.Batch)
            .HasForeignKey("ImportBatchId");
    }
}
