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
    private EntitySchemaDto[]? _cache;

    public EntitySchemaService(IServiceProvider services)
    {
        _services = services;
    }

    public EntitySchemaDto[] GetEntityList()
    {
        if (_cache is not null) return _cache;

        using var scope = _services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        _cache = context.GetEntityList();

        return _cache;
    }
}
