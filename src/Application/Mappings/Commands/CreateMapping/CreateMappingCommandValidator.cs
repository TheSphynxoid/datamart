using System;
using System.Collections.Generic;
using System.Text;
using DataMart.Application.Common.Interfaces;

namespace DataMart.Application.Mappings.Commands.CreateMapping;

public class CreateMappingCommandValidator : AbstractValidator<CreateMappingCommand>
{
    
    public CreateMappingCommandValidator(IEntitySchemaService schemaService)
    {
        var entities = schemaService.GetEntityList().Select(e => e.Name); //This is cached by the service, trival cost.
        RuleFor(v => v.Name.Trim()).NotEmpty(); //This trim is to avoid just spaces being accepted
        RuleFor(v => v.Columns).NotEmpty();
        RuleFor(v => v.TargetEntity).NotEmpty().Must(target => entities.Contains(target)).WithMessage("Target entity '{PropertyValue}' is not a valid importable entity.");
        RuleForEach(v => v.Columns).ChildRules(col =>
        {
            col.RuleFor(c => c.ColumnName).NotEmpty();
            col.RuleFor(c => c.TargetColName).NotEmpty(); //We can harden this.
        });
    }
}
