using DataMart.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DataMart.Infrastructure.Data.Configurations;

/// <summary>
/// SQL defaults for non-nullable <see cref="ClientAccount"/> columns that the user
/// may not map in their Excel import. Without defaults, the executor's binary COPY
/// (which only writes the user-mapped columns + ImportBatchId + SourceKey) leaves
/// unmapped NOT NULL columns without a value, which Postgres rejects.
///
/// These defaults also document the safe "zero" / "inactive" baseline for an
/// imported row that the user did not populate.
/// </summary>
public class ClientAccountConfiguration : IEntityTypeConfiguration<ClientAccount>
{
    public void Configure(EntityTypeBuilder<ClientAccount> builder)
    {
        builder.Property(p => p.InterestRate).HasDefaultValueSql("0");
        builder.Property(p => p.IsActive).HasDefaultValueSql("true");
        builder.Property(p => p.TransactionCount).HasDefaultValueSql("0");
    }
}
