using System;
using DataMart.Application.Common.Exceptions;
using DataMart.Application.Common.Interfaces;
using DataMart.Application.ImportFile.Common;
using DataMart.Domain.Entities;
using DataMart.Domain.Enums;

namespace DataMart.Application.EntityRules.Common;

/// <summary>
/// Authoring-time companion to <see cref="RuleEvaluator"/>: parsing the
/// string API surface into enums and checking a definition is well-formed
/// against a field type. One truth for "valid rule", used by the
/// create/update validators; the evaluator re-checks at run time so a rule
/// that slips through (or drifts after a deploy) fails loudly, never silently.
/// </summary>
internal static class RuleDefinition
{
    public record ParsedRule(RuleOperator Operator, RuleValueSource ValueSource, RuleFunction? Function);

    public static ParsedRule Parse(string op, string source, string? function)
    {
        if (!Enum.TryParse<RuleOperator>(op, ignoreCase: true, out var parsedOp))
            throw new AppValidationException($"'{op}' is not a supported operator.");
        if (!Enum.TryParse<RuleValueSource>(source, ignoreCase: true, out var parsedSource))
            throw new AppValidationException($"'{source}' is not a supported value source (Constant/Function).");

        RuleFunction? parsedFunction = null;
        if (parsedSource == RuleValueSource.Function)
        {
            if (!Enum.TryParse<RuleFunction>(function, ignoreCase: true, out var f))
                throw new AppValidationException($"'{function}' is not a supported function.");
            parsedFunction = f;
        }

        return new ParsedRule(parsedOp, parsedSource, parsedFunction);
    }

    /// <summary>Null when the definition is well-formed, else the reason.</summary>
    public static string? Check(EntityValidationRule rule, string fieldClrType)
    {
        var kind = ImportCellParser.MapType(fieldClrType);
        if (!IsOperatorValidFor(kind, rule.Operator))
            return $"'{rule.Operator}' cannot be applied to {DescribeKind(kind)} fields.";

        try
        {
            ResolveRhs(rule, kind, DateTimeOffset.UtcNow);
            return null;
        }
        catch (AppValidationException ex)
        {
            return ex.Message;
        }
    }

    public static bool IsOperatorValidFor(ImportColumnType kind, RuleOperator op) => (kind, op) switch
    {
        (ImportColumnType.Text, RuleOperator.Equals) => true,
        (ImportColumnType.Text, RuleOperator.NotEquals) => true,
        (ImportColumnType.Text, RuleOperator.Contains) => true,
        (ImportColumnType.Text, RuleOperator.In) => true,
        (ImportColumnType.Text, RuleOperator.NotIn) => true,
        (ImportColumnType.Integer, _) => IsComparisonOrMembership(op),
        (ImportColumnType.Decimal, _) => IsComparisonOrMembership(op),
        (ImportColumnType.Date, _) => IsComparisonOrMembership(op),
        (ImportColumnType.Timestamp, _) => IsComparisonOrMembership(op),
        (ImportColumnType.Boolean, RuleOperator.Equals) => true,
        (ImportColumnType.Boolean, RuleOperator.NotEquals) => true,
        (ImportColumnType.Boolean, RuleOperator.In) => true,
        (ImportColumnType.Boolean, RuleOperator.NotIn) => true,
        _ => false,
    };

    private static bool IsComparisonOrMembership(RuleOperator op) => op
        is RuleOperator.Equals or RuleOperator.NotEquals
        or RuleOperator.GreaterThan or RuleOperator.GreaterThanOrEqual
        or RuleOperator.LessThan or RuleOperator.LessThanOrEqual
        or RuleOperator.In or RuleOperator.NotIn;

    private static string DescribeKind(ImportColumnType kind) => kind switch
    {
        ImportColumnType.Text => "text",
        ImportColumnType.Integer or ImportColumnType.Decimal => "numeric",
        ImportColumnType.Date or ImportColumnType.Timestamp => "date",
        ImportColumnType.Boolean => "boolean",
        _ => "this type of",
    };

    private static void ResolveRhs(EntityValidationRule rule, ImportColumnType kind, DateTimeOffset now) =>
        RuleEvaluator.ResolveRhs(rule, kind, now);
}
