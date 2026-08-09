using System;
using System.Collections.Generic;
using System.Text;
using DataMart.Application.Common.Interfaces;

namespace DataMart.Application.Mappings.Queries.GetMappings;

public record GetMappingsQuery(string? TargetEntity, string? Search) : IRequest<List<MappingDto>>;

public class GetMappingsQueryHandler : IRequestHandler<GetMappingsQuery, List<MappingDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly IMapper _mapper;

    public GetMappingsQueryHandler(IApplicationDbContext context, IMapper mapper)
    {
        _context = context;
        _mapper = mapper;
    }
    public async Task<List<MappingDto>> Handle(GetMappingsQuery request, CancellationToken cancellationToken)
    {
        var query = _context.Mappings.AsQueryable();

        if (request.TargetEntity is not null)
        {
            query = query.Where(m => m.TargetEntity == request.TargetEntity);

            if (request.Search is not null)
                query = query.Where(m => m.Name.ToLower().Contains(request.Search.ToLower()));
        }

        return await query.ProjectTo<MappingDto>(_mapper.ConfigurationProvider).ToListAsync();
    }
}
