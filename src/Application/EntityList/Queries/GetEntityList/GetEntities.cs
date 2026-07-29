using System;
using System.Collections.Generic;
using System.Text;
using DataMart.Application.Common.Interfaces;


namespace DataMart.Application.EntityList.Queries.GetEntityList;

public record GetEntityQuery : IRequest<EntitySchemaDto[]>;

public class GetEntities : IRequestHandler<GetEntityQuery, EntitySchemaDto[]>
{
    private readonly IApplicationDbContext _context;

    public GetEntities(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<EntitySchemaDto[]> Handle(GetEntityQuery request, CancellationToken cancellationToken)
    {

        return _context.GetEntityList();
        //return [.. entities]
    }
}
