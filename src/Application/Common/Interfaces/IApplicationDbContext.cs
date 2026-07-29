using DataMart.Application.EntityList.Queries.GetEntityList;
using DataMart.Domain.Entities;
using Microsoft.EntityFrameworkCore.Metadata;

namespace DataMart.Application.Common.Interfaces;

public interface IApplicationDbContext
{
    DbSet<TodoList> TodoLists { get; }

    DbSet<TodoItem> TodoItems { get; }

    DbSet<ImportRecord> ImportRecords { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken);

    EntitySchemaDto[] GetEntityList();
}
