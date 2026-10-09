using System.Reflection;
using DataMart.Application.Common.Interfaces;
using DataMart.Application.EntityList.Queries.GetEntityList;
using DataMart.Domain.Common;
using DataMart.Domain.Entities;
using DataMart.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace DataMart.Infrastructure.Data;

public class ApplicationDbContext : IdentityDbContext<ApplicationUser>, IApplicationDbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

    public DbSet<ImportRecord> ImportRecords => Set<ImportRecord>();

    public DbSet<ImportBatch> ImportBatches => Set<ImportBatch>();

    public DbSet<ClientAccount> ClientAccounts => Set<ClientAccount>();

    public DbSet<Mapping> Mappings => Set<Mapping>();

    public DbSet<EntityValidationRule> EntityValidationRules => Set<EntityValidationRule>();

    public DbSet<CustomerTransaction> CustomerTransactions => Set<CustomerTransaction>();

    public DbSet<AccessLog> AccessLogs => Set<AccessLog>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());

        // Set database defaults for audit columns so Npgsql COPY (which bypasses EF Core)
        // still gets valid timestamps instead of NULL constraint violations.
        foreach (var entityType in builder.Model.GetEntityTypes())
        {
            if (!typeof(BaseAuditableEntity).IsAssignableFrom(entityType.ClrType))
                continue;

            var created = entityType.FindProperty(nameof(BaseAuditableEntity.Created));
            if (created is not null)
                created.SetDefaultValueSql("NOW()");

            var lastModified = entityType.FindProperty(nameof(BaseAuditableEntity.LastModified));
            if (lastModified is not null)
                lastModified.SetDefaultValueSql("NOW()");
        }
    }

    public EntitySchemaDto[] GetEntityList()
    {
        //Get All entities that inherit IImportable, which is BaseAuditableEntity inherits, and are not explicitly excluded.
        var entities = Model.GetEntityTypes()
            .Where(e => !e.IsOwned() && e.FindPrimaryKey() is not null
            && typeof(IImportable).IsAssignableFrom(e.ClrType) && !Attribute.IsDefined(e.ClrType, typeof(ExcludeAttribute), inherit: true));
        List<EntitySchemaDto> resp = [];
        foreach (var entity in entities)
        {
            //Get Properties and their details
            List<EntityPropertyDto> propDtos = [];
            foreach(var prop in entity.GetProperties().Where(p => !p.IsShadowProperty()))
            {
                //Some Reflection-like work.
                propDtos.Add(new EntityPropertyDto(
                    prop.GetColumnName(),
                    Nullable.GetUnderlyingType(prop.ClrType)?.Name ?? prop.ClrType.Name,
                    prop.IsNullable,
                    prop.GetMaxLength(),
                    prop.GetPrecision(),
                    prop.GetScale(),
                    prop.IsPrimaryKey(),
                    prop.IsForeignKey(),
                    null,
                    prop.GetContainingKeys().Any(k => !k.IsPrimaryKey()),
                    prop.PropertyInfo?.GetCustomAttribute<ExcludeAttribute>() is not null)

                );
            }
            resp.Add(new EntitySchemaDto(entity.GetTableName()!, entity.FindPrimaryKey()?.Properties.Select(p => p.Name).ToArray() ?? [], propDtos));
        }
            
        
        return resp.ToArray();
    }
        
}
