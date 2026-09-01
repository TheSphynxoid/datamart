using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DataMart.Application.Common.Interfaces;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace DataMart.Application.Mappings.Commands.UpdateMapping;

public class UpdateMappingCommandValidator : AbstractValidator<UpdateMappingCommand>
{
    public UpdateMappingCommandValidator(IEntitySchemaService schemaService, IApplicationDbContext context)
    {
        var entities = schemaService.GetEntityList().Select(e => e.Name); //This is cached by the service, trival cost.
        RuleFor(v => v.Name.Trim()).NotEmpty(); //This trim is to avoid just spaces being accepted
        RuleFor(v => v.Columns).NotEmpty();

        // Mapping name is unique per target entity. UpdateMappingCommand doesn't carry
        // TargetEntity (the target is fixed for a given mapping id), so we look up the
        // existing mapping to get it. The check excludes this mapping's own id, so a
        // no-op rename to the same name is allowed.
        RuleFor(v => v)
            .MustAsync(async (cmd, _, ct) =>
            {
                var existing = await context.Mappings
                    .Where(m => m.Id == cmd.Id)
                    .Select(m => m.TargetEntity)
                    .FirstOrDefaultAsync(ct);
                if (existing is null) return true; // The handler's Guard will surface a 404.

                return !await context.Mappings
                    .AnyAsync(m => m.Id != cmd.Id
                                && m.TargetEntity == existing
                                && m.Name.ToLower() == cmd.Name.Trim().ToLower(), ct);
            })
            .WithMessage("A mapping named '{Name}' already exists for this target entity.")
            .OverridePropertyName(nameof(UpdateMappingCommand.Name));

        RuleForEach(v => v.Columns).ChildRules(col =>
        {
            col.RuleFor(c => c.ColumnName.Trim()).NotEmpty();
            col.RuleFor(c => c.TargetColName.Trim()).NotEmpty(); //We can harden this to check for validity of properties.
        });
    }
}
