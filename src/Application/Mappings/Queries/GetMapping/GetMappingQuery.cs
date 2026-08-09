using System;
using System.Collections.Generic;
using System.Text;
using DataMart.Application.Common.Interfaces;
using DataMart.Application.Mappings.Queries.GetMappings;

namespace DataMart.Application.Mappings.Queries.GetMapping;

public record GetMappingQuery(int Id) : IRequest<MappingDto?>;

public class GetMappingQueryHandler : IRequestHandler<GetMappingQuery, MappingDto?>
{
    private readonly IApplicationDbContext _context;
    private readonly IMapper _mapper;

    public GetMappingQueryHandler(IApplicationDbContext context, IMapper mapper)
    {
        _context = context;
        _mapper = mapper;
    }
    public async Task<MappingDto?> Handle(GetMappingQuery request, CancellationToken cancellationToken)
    {
        var mapping = await _context.Mappings.FindAsync([request.Id], cancellationToken);
        return mapping is null ? null : _mapper.Map<MappingDto>(mapping);
    }
}
