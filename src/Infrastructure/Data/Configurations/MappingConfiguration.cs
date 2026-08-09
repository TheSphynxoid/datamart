using DataMart.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DataMart.Infrastructure.Data.Configurations;

public class MappingConfiguration : IEntityTypeConfiguration<Mapping>
{
    public void Configure(EntityTypeBuilder<Mapping> builder)
    {
        builder.OwnsMany(c => c.Columns, col =>
        {
            col.Property(p => p.ColumnName).HasMaxLength(200);
            col.Property(p => p.ColumnType).HasMaxLength(50);
            col.Property(p => p.TargetColName).HasMaxLength(200);
            col.Property(p => p.DateFormat).HasMaxLength(20);
            col.Property(p => p.DecimalSeparator).HasMaxLength(1);
            col.Property(p => p.BoolTrueValue).HasMaxLength(50);
            col.Property(p => p.BoolFalseValue).HasMaxLength(50);
        });
    }
}
