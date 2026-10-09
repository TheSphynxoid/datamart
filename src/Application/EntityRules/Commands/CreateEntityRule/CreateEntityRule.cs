using System;
using System.Collections.Generic;
using System.Text;
using DataMart.Application.Common.Interfaces;
using DataMart.Application.EntityRules.Common;
using DataMart.Domain.Entities;

namespace DataMart.Application.EntityRules.Commands.CreateEntityRule;

public record class CreateEntityRuleCommand(
    string TargetEntity,
    string Field,
    string Operator,
    string ValueSource,
    string? Value,
    List<string>? Values,
    string? Function,
    int? FunctionArg,
    string? Message) : IRequest<int>;

public class CreateEntityRuleCommandHandler : IRequestHandler<CreateEntityRuleCommand, int>
{
    private readonly IApplicationDbContext _context;
    private readonly IUser _user;

    public CreateEntityRuleCommandHandler(IApplicationDbContext context, IUser user)
    {
        _context = context;
        _user = user;
    }

    public async Task<int> Handle(CreateEntityRuleCommand request, CancellationToken cancellationToken)
    {
        // Parsing already succeeded in the validator; re-parse here as the
        // single truth rather than trusting validated strings.
        var parsed = RuleDefinition.Parse(request.Operator, request.ValueSource, request.Function);

        var rule = new EntityValidationRule
        {
            TargetEntity = request.TargetEntity,
            Field = request.Field,
            Operator = parsed.Operator,
            ValueSource = parsed.ValueSource,
            Value = request.Value,
            Values = request.Values ?? [],
            Function = parsed.Function,
            FunctionArg = request.FunctionArg,
            Message = request.Message,
            IsActive = true,
            CreatedBy = _user.Id ?? "unknown",
        };

        _context.EntityValidationRules.Add(rule);

        await _context.SaveChangesAsync(cancellationToken);

        return rule.Id;
    }
}
