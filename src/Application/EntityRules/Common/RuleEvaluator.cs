using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DataMart.Application.Common.Exceptions;
using DataMart.Application.Common.Interfaces;
using DataMart.Application.ImportFile.Common;
using DataMart.Domain.Entities;
using DataMart.Domain.Enums;

namespace DataMart.Application.EntityRules.Common;

/// <summary>
/// Outcome of a single rule evaluation: null means the row passed the rule.
/// ResolvedValue carries the right-hand side as evaluated (constants verbatim,
/// functions resolved against the import clock) so reports stay explainable
/// after the fact, even if the rule later changes.
/// </summary>
public record RuleFailure(int RuleId, string Field, string Message, string? ResolvedValue);

/// <summary>
/// Pure row-level rule evaluation shared by pre-flight validation and import,
/// mirroring <see cref="ImportCellParser"/>: no I/O, no DI, the clock arrives
/// as a parameter so tests pin exact dates. Locked semantics: all active rules
/// for the entity are AND-ed; a field with no covering rule imposes nothing; a
/// nullish cell skips the rule (requiredness is the schema's job, enforced
/// upstream); anything unsupported throws rather than silently passing.
/// </summary>
internal static class RuleEvaluator
{
    public static RuleFailure? Evaluate(
        EntityValidationRule rule,
        IReadOnlyDictionary<string, object?> payload,
        string fieldClrType,
        DateTimeOffset nowUtc)
    {
        if (!TryGetField(payload, rule.Field, out var raw))
            return null; // Mapping does not cover this field: nothing to judge.

        if (ImportCellParser.IsEmpty(raw))
            return null;

        var columnType = ImportCellParser.MapType(fieldClrType);
        var (rhs, display) = ResolveRhs(rule, columnType, nowUtc);

        bool passed = columnType switch
        {
            ImportColumnType.Text => CompareText(rule, ToText(raw), rhs),
            ImportColumnType.Integer => CompareComparable(rule, ToInt64(raw, rule), rhs),
            ImportColumnType.Decimal => CompareComparable(rule, ToDecimal(raw, rule), rhs),
            ImportColumnType.Date or ImportColumnType.Timestamp => CompareComparable(rule, ToUtcDateTime(raw, rule), rhs),
            ImportColumnType.Boolean => CompareComparable(rule, ToBoolean(raw, rule), rhs),
            _ => throw Fail(rule, $"validation is not supported for '{fieldClrType}' fields."),
        };

        if (passed)
            return null;

        var message = string.IsNullOrWhiteSpace(rule.Message)
            ? $"{rule.Field} must be {DescribeOperator(rule.Operator)} {display}"
            : rule.Message;
        return new RuleFailure(rule.Id, rule.Field, message, display);
    }

    /// <summary>
    /// Evaluates every active rule against one row's payload. Returns the
    /// failures (empty when the row passes all rules). Throws when a rule
    /// references a field that no longer exists on the entity (schema drift):
    /// a rule that cannot be evaluated must fail loudly, never silently pass.
    /// </summary>
    public static List<RuleFailure> ApplyRules(
        IReadOnlyList<EntityValidationRule> rules,
        IDictionary<string, string> fieldClrTypes,
        IReadOnlyDictionary<string, object?> payload,
        string targetEntity,
        DateTimeOffset nowUtc)
    {
        var failures = new List<RuleFailure>();
        foreach (var rule in rules)
        {
            if (!fieldClrTypes.TryGetValue(rule.Field, out var clrType))
                throw new AppValidationException(
                    $"Rule {rule.Id} references unknown field '{rule.Field}' on entity '{targetEntity}'; deactivate or fix the rule.");

            var failure = Evaluate(rule, payload, clrType, nowUtc);
            if (failure is not null)
                failures.Add(failure);
        }
        return failures;
    }

    private static bool TryGetField(IReadOnlyDictionary<string, object?> payload, string field, out object? value)
    {
        if (payload.TryGetValue(field, out value))
            return true;

        foreach (var kv in payload)
        {
            if (string.Equals(kv.Key, field, StringComparison.OrdinalIgnoreCase))
            {
                value = kv.Value;
                return true;
            }
        }

        value = null;
        return false;
    }

    internal static (object Rhs, string Display) ResolveRhs(
        EntityValidationRule rule, ImportColumnType columnType, DateTimeOffset nowUtc)
    {
        bool isMembership = rule.Operator is RuleOperator.In or RuleOperator.NotIn;

        if (rule.ValueSource == RuleValueSource.Function)
        {
            if (isMembership)
                throw Fail(rule, "membership rules (In/NotIn) require a value list, not a function.");
            if (columnType is not (ImportColumnType.Date or ImportColumnType.Timestamp))
                throw Fail(rule, "functions are only supported for date fields.");
            var resolved = ResolveFunction(rule, nowUtc);
            return (resolved, resolved.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        }

        if (isMembership)
        {
            if (rule.Values is null || rule.Values.Count == 0)
                throw Fail(rule, "membership rules (In/NotIn) require at least one value.");
            var list = CoerceList(rule, columnType);
            var shown = ((System.Collections.IEnumerable)list).Cast<object>().Select(FormatScalar);
            return (list, "[" + string.Join(", ", shown) + "]");
        }

        if (rule.Value is null)
            throw Fail(rule, "a constant value is required.");
        var scalar = CoerceScalar(rule.Value, columnType, rule);
        return (scalar, FormatScalar(scalar));
    }

    private static DateTime ResolveFunction(EntityValidationRule rule, DateTimeOffset nowUtc)
    {
        var utc = nowUtc.UtcDateTime;
        return rule.Function switch
        {
            RuleFunction.Today => utc.Date,
            RuleFunction.Now => utc,
            RuleFunction.TodayMinusYears when rule.FunctionArg.HasValue => utc.Date.AddYears(-rule.FunctionArg.Value),
            RuleFunction.TodayMinusDays when rule.FunctionArg.HasValue => utc.Date.AddDays(-rule.FunctionArg.Value),
            RuleFunction.TodayMinusYears or RuleFunction.TodayMinusDays => throw Fail(rule, $"'{rule.Function}' requires an argument."),
            null => throw Fail(rule, "a function is required."),
            _ => throw Fail(rule, $"'{rule.Function}' is not a supported function."),
        };
    }

    private static object CoerceScalar(string value, ImportColumnType columnType, EntityValidationRule rule)
    {
        var trimmed = value.Trim();
        return columnType switch
        {
            ImportColumnType.Text => trimmed,
            ImportColumnType.Integer => long.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l)
                ? l : throw Fail(rule, $"'{value}' is not a valid integer."),
            ImportColumnType.Decimal => decimal.TryParse(trimmed, NumberStyles.Number, CultureInfo.InvariantCulture, out var m)
                ? m : throw Fail(rule, $"'{value}' is not a valid number."),
            ImportColumnType.Date or ImportColumnType.Timestamp
                => DateTime.TryParse(trimmed, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt)
                ? dt : throw Fail(rule, $"'{value}' is not a valid date."),
            ImportColumnType.Boolean => bool.TryParse(trimmed, out var b)
                ? b : throw Fail(rule, $"'{value}' is not a valid boolean (true/false)."),
            _ => throw Fail(rule, "this field type cannot carry a rule value."),
        };
    }

    private static object CoerceList(EntityValidationRule rule, ImportColumnType columnType)
    {
        return columnType switch
        {
            ImportColumnType.Text => rule.Values.Select(v => v.Trim()).ToList(),
            ImportColumnType.Integer => rule.Values.Select(v =>
            {
                if (!long.TryParse(v.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var l))
                    throw Fail(rule, $"'{v}' is not a valid integer.");
                return l;
            }).ToList(),
            ImportColumnType.Decimal => rule.Values.Select(v =>
            {
                if (!decimal.TryParse(v.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var m))
                    throw Fail(rule, $"'{v}' is not a valid number.");
                return m;
            }).ToList(),
            ImportColumnType.Date or ImportColumnType.Timestamp => rule.Values.Select(v =>
            {
                if (!DateTime.TryParse(v.Trim(), CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt))
                    throw Fail(rule, $"'{v}' is not a valid date.");
                return dt;
            }).ToList(),
            ImportColumnType.Boolean => rule.Values.Select(v =>
            {
                if (!bool.TryParse(v.Trim(), out var b))
                    throw Fail(rule, $"'{v}' is not a valid boolean (true/false).");
                return b;
            }).ToList(),
            _ => throw Fail(rule, "this field type cannot carry a rule value list."),
        };
    }

    private static bool CompareComparable<T>(EntityValidationRule rule, T lhs, object rhs) where T : IComparable<T>
    {
        return rule.Operator switch
        {
            RuleOperator.Equals => lhs.CompareTo((T)rhs) == 0,
            RuleOperator.NotEquals => lhs.CompareTo((T)rhs) != 0,
            RuleOperator.GreaterThan => lhs.CompareTo((T)rhs) > 0,
            RuleOperator.GreaterThanOrEqual => lhs.CompareTo((T)rhs) >= 0,
            RuleOperator.LessThan => lhs.CompareTo((T)rhs) < 0,
            RuleOperator.LessThanOrEqual => lhs.CompareTo((T)rhs) <= 0,
            RuleOperator.In => ((List<T>)rhs).Contains(lhs),
            RuleOperator.NotIn => !((List<T>)rhs).Contains(lhs),
            _ => throw Fail(rule, $"'{rule.Operator}' is not a valid operator for this field type."),
        };
    }

    private static bool CompareText(EntityValidationRule rule, string lhs, object rhs)
    {
        return rule.Operator switch
        {
            RuleOperator.Equals => string.Equals(lhs, (string)rhs, StringComparison.OrdinalIgnoreCase),
            RuleOperator.NotEquals => !string.Equals(lhs, (string)rhs, StringComparison.OrdinalIgnoreCase),
            RuleOperator.Contains => lhs.Contains((string)rhs, StringComparison.OrdinalIgnoreCase),
            RuleOperator.In => ((List<string>)rhs).Contains(lhs, StringComparer.OrdinalIgnoreCase),
            RuleOperator.NotIn => !((List<string>)rhs).Contains(lhs, StringComparer.OrdinalIgnoreCase),
            _ => throw Fail(rule, $"'{rule.Operator}' is not a valid operator for text fields."),
        };
    }

    private static string ToText(object? raw) => raw?.ToString()?.Trim() ?? string.Empty;

    private static long ToInt64(object? raw, EntityValidationRule rule) => raw switch
    {
        int i => i,
        long l => l,
        _ => throw Fail(rule, $"unexpected value '{raw}' for a numeric field."),
    };

    private static decimal ToDecimal(object? raw, EntityValidationRule rule) => raw switch
    {
        decimal m => m,
        int i => i,
        long l => l,
        double d => (decimal)d,
        _ => throw Fail(rule, $"unexpected value '{raw}' for a numeric field."),
    };

    private static DateTime ToUtcDateTime(object? raw, EntityValidationRule rule) => raw switch
    {
        DateTime dt => dt.Kind switch
        {
            DateTimeKind.Utc => dt,
            DateTimeKind.Local => dt.ToUniversalTime(),
            _ => DateTime.SpecifyKind(dt, DateTimeKind.Utc),
        },
        DateOnly d => d.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
        _ => throw Fail(rule, $"unexpected value '{raw}' for a date field."),
    };

    private static bool ToBoolean(object? raw, EntityValidationRule rule) => raw switch
    {
        bool b => b,
        _ => throw Fail(rule, $"unexpected value '{raw}' for a boolean field."),
    };

    private static string FormatScalar(object? scalar) => scalar switch
    {
        DateTime dt => dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => scalar?.ToString() ?? string.Empty,
    };

    private static string DescribeOperator(RuleOperator op) => op switch
    {
        RuleOperator.Equals => "==",
        RuleOperator.NotEquals => "!=",
        RuleOperator.GreaterThan => ">",
        RuleOperator.GreaterThanOrEqual => ">=",
        RuleOperator.LessThan => "<",
        RuleOperator.LessThanOrEqual => "<=",
        RuleOperator.Contains => "containing",
        RuleOperator.In => "one of",
        RuleOperator.NotIn => "not one of",
        _ => op.ToString(),
    };

    private static AppValidationException Fail(EntityValidationRule rule, string message) =>
        rule.Id == 0
            ? new($"Rule ('{rule.Field}'): {message}")
            : new($"Rule {rule.Id} ('{rule.Field}'): {message}");
}
