using System;
using System.Collections.Generic;
using System.Text;
using DataMart.Application.Common.Interfaces;

namespace DataMart.Application.EntityRules.Queries.GetEntityRules;

public record GetEntityRulesQuery(string TargetEntity, bool ActiveOnly) : IRequest<List<EntityValidationRuleDto>>;

public class GetEntityRulesQueryHandler : IRequestHandler<GetEntityRulesQuery, List<EntityValidationRuleDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly IMapper _mapper;

    public GetEntityRulesQueryHandler(IApplicationDbContext context, IMapper mapper)
    {
        _context = context;
        _mapper = mapper;
    }

    public async Task<List<EntityValidationRuleDto>> Handle(GetEntityRulesQuery request, CancellationToken cancellationToken)
    {
        var query = _context.EntityValidationRules
            .Where(r => r.TargetEntity == request.TargetEntity);

        if (request.ActiveOnly)
            query = query.Where(r => r.IsActive);

        // Materialise before mapping: the enum-to-string conversions in the
        // profile are client-evaluated and must not be translated to SQL.
        var rules = await query
            .OrderBy(r => r.Field)
            .ThenBy(r => r.Id)
            .ToListAsync(cancellationToken);

        return _mapper.Map<List<EntityValidationRuleDto>>(rules);
    }
}
