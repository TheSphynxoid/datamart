using DataMart.Application.EntityList.Queries.GetEntityList;
using DataMart.Domain.Entities;
using Microsoft.EntityFrameworkCore.Metadata;

namespace DataMart.Application.Common.Interfaces;

public interface IApplicationDbContext
{
    DbSet<ImportRecord> ImportRecords { get; }

    DbSet<ImportBatch> ImportBatches { get; }

    DbSet<ClientAccount> ClientAccounts { get; }

    DbSet<Mapping> Mappings { get; }

    DbSet<CustomerTransaction> CustomerTransactions { get; }

    DbSet<AccessLog> AccessLogs { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken);

    EntitySchemaDto[] GetEntityList();
}
