using System;
using System.Collections.Generic;
using System.Text;
using DataMart.Application.Common.Interfaces;
using DataMart.Domain.Entities;

namespace DataMart.Application.Mappings.Commands.CreateMapping;

public record class CreateMappingCommand(string Name, string? Description, string TargetEntity, List<ColumnMappingDto> Columns) : IRequest<int>;

public class CreateMappingCommandHandler : IRequestHandler<CreateMappingCommand, int>
{
    private readonly IApplicationDbContext _context;
    private readonly IUser _user;
    public CreateMappingCommandHandler(IApplicationDbContext context, IUser user)
    {
        _context = context;
        _user = user;
    }
    public async Task<int> Handle(CreateMappingCommand request, CancellationToken cancellationToken)
    {
        var map = new Mapping
        {
            Name = request.Name,
            Description = request.Description,
            TargetEntity = request.TargetEntity,
            Columns = request.Columns.ConvertAll(c => new ColumnMapping
            {
                ColumnName = c.ColumnName,
                ColumnType = c.ColumnType,
                TargetColName = c.TargetColName,
                IsRequired = c.IsRequired,
                DateFormat = c.DateFormat,
                TimeFormat = c.TimeFormat,
                DecimalSeparator = c.DecimalSeparator,
                StripSymbols = c.StripSymbols,
                BoolTrueValue = c.BoolTrueValue,
                BoolFalseValue = c.BoolFalseValue,
            }),
            CreatedBy = _user.Id ?? "unknown"
        };

        _context.Mappings.Add(map);

        await _context.SaveChangesAsync(cancellationToken);

        return map.Id;
    }
}
