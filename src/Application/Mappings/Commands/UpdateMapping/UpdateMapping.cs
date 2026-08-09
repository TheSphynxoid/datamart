using System;
using System.Collections.Generic;
using System.Text;
using DataMart.Application.Common.Interfaces;
using DataMart.Domain.Entities;

namespace DataMart.Application.Mappings.Commands.UpdateMapping;

public record class UpdateMappingCommand(int Id, string Name, string? Description, List<ColumnMappingDto> Columns) : IRequest;

public class UpdateMappingCommandHandler : IRequestHandler<UpdateMappingCommand>
{

    private readonly IApplicationDbContext _context;
    private readonly IMapper _mapper;
    public UpdateMappingCommandHandler(IMapper mapper, IApplicationDbContext context)
    {
        _context = context;
        _mapper = mapper;
    }

    public async Task Handle(UpdateMappingCommand request, CancellationToken cancellationToken)
    {
        var entity = await _context.Mappings
            .FindAsync([request.Id], cancellationToken);

        Guard.Against.NotFound(request.Id, entity);

        entity.Name = request.Name;
        entity.Description = request.Description;
        entity.Columns.Clear();
        entity.Columns.AddRange(request.Columns.Select(_mapper.Map<ColumnMapping>));

        await _context.SaveChangesAsync(cancellationToken);

    }
}
