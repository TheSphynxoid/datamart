using System;
using System.Collections.Generic;
using DataMart.Application.Common.Exceptions;
using DataMart.Application.EntityRules.Common;
using DataMart.Domain.Entities;
using DataMart.Domain.Enums;
using Shouldly;
using Xunit;

namespace DataMart.Application.UnitTests.EntityRules;

/// <summary>
/// Coverage for the shared rule-evaluation primitives. These run the same
/// checks as both validation and import, so a regression here breaks both.
/// The clock is a parameter, so every date test pins an exact "now".
/// </summary>
public class RuleEvaluatorTests
{
    private static readonly DateTimeOffset PinnedNow =
        new(2026, 9, 16, 12, 0, 0, TimeSpan.Zero);

    private static EntityValidationRule Age18Rule(int id = 1) => new()
    {
        Id = id,
        TargetEntity = "CustomerTransaction",
        Field = "CustomerDOB",
        Operator = RuleOperator.LessThanOrEqual,
        ValueSource = RuleValueSource.Function,
        Function = RuleFunction.TodayMinusYears,
        FunctionArg = 18,
        IsActive = true,
    };

    private static EntityValidationRule GenderRule() => new()
    {
        Id = 2,
        TargetEntity = "CustomerTransaction",
        Field = "CustGender",
        Operator = RuleOperator.In,
        ValueSource = RuleValueSource.Constant,
        Values = ["M", "F"],
        IsActive = true,
    };

    [Fact]
    public void Evaluate_AgeExactly18_Passes()
    {
        var payload = new Dictionary<string, object?> { ["CustomerDOB"] = new DateTime(2008, 9, 16) };

        RuleEvaluator.Evaluate(Age18Rule(), payload, "DateTime", PinnedNow).ShouldBeNull();
    }

    [Fact]
    public void Evaluate_AgeOver18_Passes()
    {
        var payload = new Dictionary<string, object?> { ["CustomerDOB"] = new DateTime(1990, 1, 1) };

        RuleEvaluator.Evaluate(Age18Rule(), payload, "DateTime", PinnedNow).ShouldBeNull();
    }

    [Fact]
    public void Evaluate_AgeUnder18_FailsWithResolvedBoundary()
    {
        var payload = new Dictionary<string, object?> { ["CustomerDOB"] = new DateTime(2008, 9, 17) };

        var failure = RuleEvaluator.Evaluate(Age18Rule(), payload, "DateTime", PinnedNow);

        failure.ShouldNotBeNull();
        failure.Field.ShouldBe("CustomerDOB");
        failure.ResolvedValue.ShouldBe("2008-09-16");
        failure.Message.ShouldBe("CustomerDOB must be <= 2008-09-16");
    }

    [Fact]
    public void Evaluate_CustomMessage_UsedWhenSet()
    {
        var rule = Age18Rule();
        rule.Message = "Must be 18 or older";
        var payload = new Dictionary<string, object?> { ["CustomerDOB"] = new DateTime(2020, 5, 5) };

        var failure = RuleEvaluator.Evaluate(rule, payload, "DateTime", PinnedNow);

        failure.ShouldNotBeNull();
        failure.Message.ShouldBe("Must be 18 or older");
        failure.ResolvedValue.ShouldBe("2008-09-16");
    }

    [Theory]
    [InlineData("M")]
    [InlineData("m")]
    [InlineData(" F ")]
    public void Evaluate_GenderInSet_PassesCaseInsensitiveTrimmed(string gender)
    {
        var payload = new Dictionary<string, object?> { ["CustGender"] = gender };

        RuleEvaluator.Evaluate(GenderRule(), payload, "String", PinnedNow).ShouldBeNull();
    }

    [Fact]
    public void Evaluate_GenderOutsideSet_Fails()
    {
        var payload = new Dictionary<string, object?> { ["CustGender"] = "X" };

        var failure = RuleEvaluator.Evaluate(GenderRule(), payload, "String", PinnedNow);

        failure.ShouldNotBeNull();
        failure.Message.ShouldBe("CustGender must be one of [M, F]");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("n/a")]
    public void Evaluate_NullishCell_SkipsRule(object? value)
    {
        var payload = new Dictionary<string, object?> { ["CustGender"] = value };

        RuleEvaluator.Evaluate(GenderRule(), payload, "String", PinnedNow).ShouldBeNull();
    }

    [Fact]
    public void Evaluate_FieldNotInPayload_SkipsRule()
    {
        var payload = new Dictionary<string, object?> { ["SomethingElse"] = "x" };

        RuleEvaluator.Evaluate(GenderRule(), payload, "String", PinnedNow).ShouldBeNull();
    }

    [Fact]
    public void Evaluate_FieldNameCaseMismatch_StillMatches()
    {
        var payload = new Dictionary<string, object?> { ["custgender"] = "M" };

        RuleEvaluator.Evaluate(GenderRule(), payload, "String", PinnedNow).ShouldBeNull();
    }

    [Fact]
    public void Evaluate_NumericConstant_Compares()
    {
        var rule = new EntityValidationRule
        {
            Id = 3,
            TargetEntity = "CustomerTransaction",
            Field = "TransactionAmount",
            Operator = RuleOperator.GreaterThanOrEqual,
            ValueSource = RuleValueSource.Constant,
            Value = "0",
            IsActive = true,
        };

        RuleEvaluator.Evaluate(rule,
            new Dictionary<string, object?> { ["TransactionAmount"] = 10.5m },
            "Decimal", PinnedNow).ShouldBeNull();

        var failure = RuleEvaluator.Evaluate(rule,
            new Dictionary<string, object?> { ["TransactionAmount"] = -0.01m },
            "Decimal", PinnedNow);

        failure.ShouldNotBeNull();
        failure.Message.ShouldBe("TransactionAmount must be >= 0");
    }

    [Fact]
    public void Evaluate_EmptyInList_ThrowsValidationException()
    {
        var rule = GenderRule();
        rule.Values = [];

        Should.Throw<AppValidationException>(() =>
            RuleEvaluator.Evaluate(rule,
                new Dictionary<string, object?> { ["CustGender"] = "M" },
                "String", PinnedNow));
    }

    [Fact]
    public void Evaluate_FunctionOnTextField_ThrowsValidationException()
    {
        var rule = GenderRule();
        rule.ValueSource = RuleValueSource.Function;
        rule.Function = RuleFunction.Today;

        Should.Throw<AppValidationException>(() =>
            RuleEvaluator.Evaluate(rule,
                new Dictionary<string, object?> { ["CustGender"] = "M" },
                "String", PinnedNow));
    }

    [Fact]
    public void Evaluate_FunctionWithoutArg_ThrowsValidationException()
    {
        var rule = Age18Rule();
        rule.FunctionArg = null;

        Should.Throw<AppValidationException>(() =>
            RuleEvaluator.Evaluate(rule,
                new Dictionary<string, object?> { ["CustomerDOB"] = new DateTime(1990, 1, 1) },
                "DateTime", PinnedNow));
    }

    [Fact]
    public void Evaluate_UnparseableConstant_ThrowsValidationException()
    {
        var rule = new EntityValidationRule
        {
            Id = 4,
            TargetEntity = "CustomerTransaction",
            Field = "TransactionAmount",
            Operator = RuleOperator.GreaterThan,
            ValueSource = RuleValueSource.Constant,
            Value = "not-a-number",
            IsActive = true,
        };

        Should.Throw<AppValidationException>(() =>
            RuleEvaluator.Evaluate(rule,
                new Dictionary<string, object?> { ["TransactionAmount"] = 5m },
                "Decimal", PinnedNow));
    }

    [Fact]
    public void Evaluate_WrongOperatorForText_ThrowsValidationException()
    {
        var rule = GenderRule();
        rule.Operator = RuleOperator.GreaterThan;
        rule.ValueSource = RuleValueSource.Constant;
        rule.Value = "M";
        rule.Values = [];

        Should.Throw<AppValidationException>(() =>
            RuleEvaluator.Evaluate(rule,
                new Dictionary<string, object?> { ["CustGender"] = "M" },
                "String", PinnedNow));
    }
}
