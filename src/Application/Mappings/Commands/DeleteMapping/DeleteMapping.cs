using System;
using System.Collections.Generic;
using System.Text;
using DataMart.Application.Common.Interfaces;

namespace DataMart.Application.Mappings.Commands.DeleteMapping;

public record class DeleteMappingCommand(int Id) : IRequest;

public class DeleteMappingCommandHandler : IRequestHandler<DeleteMappingCommand>
{
    private readonly IApplicationDbContext _context;
    public DeleteMappingCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task Handle(DeleteMappingCommand request, CancellationToken cancellationToken)
    {
        var entity = await _context.Mappings
            .FindAsync([request.Id], cancellationToken);

        Guard.Against.NotFound(request.Id, entity);

        _context.Mappings.Remove(entity);

        await _context.SaveChangesAsync(cancellationToken);
    }
}
