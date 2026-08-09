using System;
using System.Collections.Generic;
using System.Text;
using DataMart.Application.Common.Interfaces;

namespace DataMart.Application.Mappings.Commands.UpdateMapping;

public class UpdateMappingCommandValidator : AbstractValidator<UpdateMappingCommand>
{
    public UpdateMappingCommandValidator(IEntitySchemaService schemaService)
    {
        var entities = schemaService.GetEntityList().Select(e => e.Name); //This is cached by the service, trival cost.
        RuleFor(v => v.Name.Trim()).NotEmpty(); //This trim is to avoid just spaces being accepted
        RuleFor(v => v.Columns).NotEmpty();
        RuleForEach(v => v.Columns).ChildRules(col =>
        {
            col.RuleFor(c => c.ColumnName.Trim()).NotEmpty();
            col.RuleFor(c => c.TargetColName.Trim()).NotEmpty(); //We can harden this to check for validity of properties.
        });
    }
}
