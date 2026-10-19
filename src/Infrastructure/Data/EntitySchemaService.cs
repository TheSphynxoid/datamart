using System;
using System.Collections.Generic;
using System.Text;
using DataMart.Application.Common.Interfaces;
using DataMart.Application.EntityList.Queries.GetEntityList;
using DataMart.Domain.Common;
using Microsoft.Extensions.DependencyInjection;

namespace DataMart.Infrastructure.Data;

public class EntitySchemaService : IEntitySchemaService
{
    private readonly IServiceProvider _services;
    private readonly object _gate = new();
    private EntitySchemaDto[]? _cache;

    public EntitySchemaService(IServiceProvider services)
    {
        _services = services;
    }

    public EntitySchemaDto[] GetEntityList()
    {
        // Imports may now run concurrently (per-table scheduling): the first-call
        // reflection pass is guarded so parallel executors don't race the cache.
        // The cached array is treated as read-only by every consumer.
        lock (_gate)
        {
            if (_cache is not null) return _cache;

            using var scope = _services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
            _cache = context.GetEntityList();

            return _cache;
        }
    }
}
