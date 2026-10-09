using System;
using System.Collections.Generic;
using System.Text;
using DataMart.Application.Common.Interfaces;
using DataMart.Application.EntityRules.Common;
using DataMart.Domain.Entities;

namespace DataMart.Application.EntityRules.Commands.UpdateEntityRule;

public record class UpdateEntityRuleCommand(
    int Id,
    string TargetEntity,
    string Field,
    string Operator,
    string ValueSource,
    string? Value,
    List<string>? Values,
    string? Function,
    int? FunctionArg,
    string? Message,
    bool IsActive) : IRequest;

public class UpdateEntityRuleCommandHandler : IRequestHandler<UpdateEntityRuleCommand>
{
    private readonly IApplicationDbContext _context;

    public UpdateEntityRuleCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task Handle(UpdateEntityRuleCommand request, CancellationToken cancellationToken)
    {
        var rule = await _context.EntityValidationRules
            .FindAsync([request.Id], cancellationToken);

        Guard.Against.NotFound(request.Id, rule);

        var parsed = RuleDefinition.Parse(request.Operator, request.ValueSource, request.Function);

        rule.TargetEntity = request.TargetEntity;
        rule.Field = request.Field;
        rule.Operator = parsed.Operator;
        rule.ValueSource = parsed.ValueSource;
        rule.Value = request.Value;
        rule.Values = request.Values ?? [];
        rule.Function = parsed.Function;
        rule.FunctionArg = request.FunctionArg;
        rule.Message = request.Message;
        rule.IsActive = request.IsActive;

        await _context.SaveChangesAsync(cancellationToken);
    }
}
