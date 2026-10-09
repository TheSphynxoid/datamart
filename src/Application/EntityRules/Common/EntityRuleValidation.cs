using System;
using System.Collections.Generic;
using System.Linq;
using DataMart.Application.Common.Exceptions;
using DataMart.Application.Common.Interfaces;
using DataMart.Domain.Entities;
using DataMart.Domain.Enums;
using FluentValidation;

namespace DataMart.Application.EntityRules.Common;

/// <summary>
/// The string fields every rule command carries (create and update share the
/// shape; update adds Id + IsActive). Validators project their command onto
/// this draft and share one implementation below.
/// </summary>
internal record RuleDraft(
    string TargetEntity,
    string Field,
    string Operator,
    string ValueSource,
    string? Value,
    List<string>? Values,
    string? Function,
    int? FunctionArg,
    string? Message);

/// <summary>
/// Meta-validation, run at authoring time so a bad rule fails here and never
/// at import time: the entity must be importable, the field must exist and not
/// be [Exclude]d, the operator must fit the field type, and the value must
/// parse. A stored rule is therefore always complete and enforceable.
/// </summary>
internal static class EntityRuleValidation
{
    public static void AddDefinitionRules<T>(AbstractValidator<T> validator, Func<T, RuleDraft> draftOf, IEntitySchemaService schema)
    {
        // Cached server-lifetime by the service: trivial cost, same as mappings.
        var entities = schema.GetEntityList()
            .ToDictionary(e => e.Name, e => e, StringComparer.OrdinalIgnoreCase);

        validator.RuleFor(x => draftOf(x).TargetEntity)
            .NotEmpty().MaximumLength(200)
            .Must(t => entities.ContainsKey(t))
            .WithMessage("Target entity '{PropertyValue}' is not a valid importable entity.");

        validator.RuleFor(x => draftOf(x).Field)
            .NotEmpty().MaximumLength(200);

        validator.RuleFor(x => draftOf(x).Value).MaximumLength(500);
        validator.RuleFor(x => draftOf(x).Message).MaximumLength(500);

        validator.RuleFor(x => draftOf(x)).Custom((draft, ctx) =>
        {
            if (!entities.TryGetValue(draft.TargetEntity, out var entity))
                return;

            var property = entity.Properties?.FirstOrDefault(p =>
                string.Equals(p.Name, draft.Field, StringComparison.OrdinalIgnoreCase));
            if (property is null)
            {
                ctx.AddFailure("Field", $"'{draft.Field}' is not a field of '{draft.TargetEntity}'.");
                return;
            }

            if (property.IsExcluded)
            {
                ctx.AddFailure("Field", $"'{draft.Field}' is a system field and cannot carry a rule.");
                return;
            }

            RuleDefinition.ParsedRule parsed;
            try
            {
                parsed = RuleDefinition.Parse(draft.Operator, draft.ValueSource, draft.Function);
            }
            catch (AppValidationException ex)
            {
                ctx.AddFailure("Operator", ex.Message);
                return;
            }

            // Exactly one value channel per shape: stray data is rejected
            // rather than silently dropped, so the stored rule is unambiguous.
            bool membership = parsed.Operator is RuleOperator.In or RuleOperator.NotIn;
            bool shapeInvalid = false;
            if (parsed.ValueSource == RuleValueSource.Function)
            {
                if (!string.IsNullOrWhiteSpace(draft.Value))
                {
                    ctx.AddFailure("Value", "Value must be empty for function rules.");
                    shapeInvalid = true;
                }
                if (draft.Values is { Count: > 0 })
                {
                    ctx.AddFailure("Values", "Values must be empty for function rules.");
                    shapeInvalid = true;
                }
            }
            else if (membership)
            {
                if (!string.IsNullOrWhiteSpace(draft.Value))
                {
                    ctx.AddFailure("Value", "Value must be empty for In/NotIn rules; use Values.");
                    shapeInvalid = true;
                }
                if (!string.IsNullOrWhiteSpace(draft.Function))
                {
                    ctx.AddFailure("Function", "Function must be empty for In/NotIn rules.");
                    shapeInvalid = true;
                }
            }
            else if (!string.IsNullOrWhiteSpace(draft.Function))
            {
                ctx.AddFailure("Function", "Function must be empty for constant rules.");
                shapeInvalid = true;
            }
            else if (draft.Values is { Count: > 0 })
            {
                ctx.AddFailure("Values", "Values must be empty unless the operator is In/NotIn.");
                shapeInvalid = true;
            }

            if (shapeInvalid)
                return;

            var rule = new EntityValidationRule
            {
                TargetEntity = draft.TargetEntity,
                Field = draft.Field,
                Operator = parsed.Operator,
                ValueSource = parsed.ValueSource,
                Value = draft.Value,
                Values = draft.Values ?? [],
                Function = parsed.Function,
                FunctionArg = draft.FunctionArg,
                Message = draft.Message,
            };

            var problem = RuleDefinition.Check(rule, property.DataType ?? "String");
            if (problem is not null)
                ctx.AddFailure("Field", problem);
        });
    }
}
