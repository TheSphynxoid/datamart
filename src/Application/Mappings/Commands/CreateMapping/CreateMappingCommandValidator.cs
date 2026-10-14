using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DataMart.Application.Common.Interfaces;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace DataMart.Application.Mappings.Commands.CreateMapping;

public class CreateMappingCommandValidator : AbstractValidator<CreateMappingCommand>
{

    public CreateMappingCommandValidator(IEntitySchemaService schemaService, IApplicationDbContext context)
    {
        var entities = schemaService.GetEntityList().Select(e => e.Name); //This is cached by the service, trival cost.
        RuleFor(v => v.Name.Trim()).NotEmpty(); //This trim is to avoid just spaces being accepted
        RuleFor(v => v.Columns).NotEmpty();
        RuleFor(v => v.TargetEntity).NotEmpty().Must(target => entities.Contains(target)).WithMessage("Target entity '{PropertyValue}' is not a valid importable entity.");

        // Mapping name is unique per target entity (case-insensitive, trimmed).
        // This is the spec's "enregistrée par banque / par entité" requirement.
        RuleFor(v => v)
            .MustAsync(async (cmd, _, ct) => !await context.Mappings
                .AnyAsync(m => m.TargetEntity == cmd.TargetEntity
                            && m.Name.ToLower() == cmd.Name.Trim().ToLower(), ct))
            .WithMessage("A mapping named '{Name}' already exists for this target entity.")
            .OverridePropertyName(nameof(CreateMappingCommand.Name));

        RuleForEach(v => v.Columns).ChildRules(col =>
        {
            col.RuleFor(c => c.ColumnName).NotEmpty();
            // TargetColName becomes a SQL identifier in the COPY statement: allow only
            // plain C#-style identifiers, so nothing crafted client-side can alter the
            // statement's structure. (BulkImporter additionally quotes/escapes.)
            col.RuleFor(c => c.TargetColName)
                .NotEmpty()
                .Matches("^[A-Za-z_][A-Za-z0-9_]*$")
                .WithMessage("Target column '{PropertyValue}' must contain only letters, digits and underscores.");
        });
    }
}
