using DataMart.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DataMart.Infrastructure.Data.Configurations;

public class EntityValidationRuleConfiguration : IEntityTypeConfiguration<EntityValidationRule>
{
    public void Configure(EntityTypeBuilder<EntityValidationRule> builder)
    {
        builder.Property(r => r.TargetEntity).HasMaxLength(200);
        builder.Property(r => r.Field).HasMaxLength(200);
        builder.Property(r => r.Value).HasMaxLength(500);
        builder.Property(r => r.Message).HasMaxLength(500);
    }
}
