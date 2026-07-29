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
        builder.Property(t => t.Description)
            .HasMaxLength(200)
            .IsRequired();
    }
}
