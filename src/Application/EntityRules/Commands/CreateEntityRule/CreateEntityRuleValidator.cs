using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DataMart.Application.Common.Interfaces;
using DataMart.Application.EntityRules.Common;
using FluentValidation;

namespace DataMart.Application.EntityRules.Commands.CreateEntityRule;

public class CreateEntityRuleCommandValidator : AbstractValidator<CreateEntityRuleCommand>
{
    public CreateEntityRuleCommandValidator(IEntitySchemaService schema)
    {
        EntityRuleValidation.AddDefinitionRules(this, cmd => new RuleDraft(
            cmd.TargetEntity, cmd.Field, cmd.Operator, cmd.ValueSource,
            cmd.Value, cmd.Values, cmd.Function, cmd.FunctionArg, cmd.Message), schema);
    }
}
