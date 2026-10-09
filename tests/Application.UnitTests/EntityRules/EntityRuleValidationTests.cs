using System.Collections.Generic;
using DataMart.Application.Common.Interfaces;
using DataMart.Application.EntityList.Queries.GetEntityList;
using DataMart.Application.EntityRules.Commands.CreateEntityRule;
using FluentValidation;
using Moq;
using Shouldly;
using Xunit;

namespace DataMart.Application.UnitTests.EntityRules;

/// <summary>
/// Meta-validation must reject bad rules at authoring time so they can never
/// fail at import time. The schema service is mocked; the funnel is the same
/// cached GetEntityList() the production validator uses.
/// </summary>
public class EntityRuleValidationTests
{
    private static IEntitySchemaService SchemaService()
    {
        var schema = new Mock<IEntitySchemaService>();
        schema.Setup(s => s.GetEntityList()).Returns([
            new EntitySchemaDto("CustomerTransactions", ["Id"], [
                new EntityPropertyDto("Id", "Int32", false, null, null, null, true, false, null, true, false),
                new EntityPropertyDto("CustomerDOB", "DateTime", false, null, null, null, false, false, null, false, false),
                new EntityPropertyDto("CustGender", "String", true, null, null, null, false, false, null, false, false),
                new EntityPropertyDto("TransactionAmount", "Decimal", false, null, null, null, false, false, null, false, false),
                new EntityPropertyDto("Created", "DateTimeOffset", false, null, null, null, false, false, null, false, true),
            ]),
        ]);
        return schema.Object;
    }

    private static CreateEntityRuleCommandValidator Validator() =>
        new(SchemaService());

    private static CreateEntityRuleCommand Constant(
        string field = "TransactionAmount",
        string op = "GreaterThanOrEqual",
        string? value = "0") => new(
            "CustomerTransactions", field, op, "Constant",
            value, null, null, null, null);

    [Fact]
    public void Validate_WellFormedConstantRule_IsValid()
    {
        Validator().Validate(Constant()).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Validate_WellFormedFunctionRule_IsValid()
    {
        var command = new CreateEntityRuleCommand(
            "CustomerTransactions", "CustomerDOB", "LessThanOrEqual", "Function",
            null, null, "TodayMinusYears", 18, null);

        Validator().Validate(command).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Validate_WellFormedInRule_IsValid()
    {
        var command = new CreateEntityRuleCommand(
            "CustomerTransactions", "CustGender", "In", "Constant",
            null, ["M", "F"], null, null, null);

        Validator().Validate(command).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Validate_UnknownEntity_IsInvalid()
    {
        var result = Validator().Validate(Constant() with { TargetEntity = "Nope" });

        result.IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Validate_UnknownField_IsInvalid()
    {
        var result = Validator().Validate(Constant("Nope") with { Value = "0" });

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.ErrorMessage.Contains("is not a field of"));
    }

    [Fact]
    public void Validate_ExcludedField_IsInvalid()
    {
        var result = Validator().Validate(Constant("Created"));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.ErrorMessage.Contains("system field"));
    }

    [Fact]
    public void Validate_TextOperatorOnDate_IsInvalid()
    {
        var result = Validator().Validate(Constant("CustomerDOB", "Contains", "2024"));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.ErrorMessage.Contains("cannot be applied to date fields"));
    }

    [Fact]
    public void Validate_EmptyInList_IsInvalid()
    {
        var command = new CreateEntityRuleCommand(
            "CustomerTransactions", "CustGender", "In", "Constant",
            null, [], null, null, null);

        var result = Validator().Validate(command);

        result.IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Validate_FunctionOnTextField_IsInvalid()
    {
        var command = new CreateEntityRuleCommand(
            "CustomerTransactions", "CustGender", "Equals", "Function",
            null, null, "Today", null, null);

        var result = Validator().Validate(command);

        result.IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Validate_UnparseableConstant_IsInvalid()
    {
        var result = Validator().Validate(Constant(value: "not-a-number"));

        result.IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Validate_ValueAndFunctionBothSet_IsInvalid()
    {
        var command = new CreateEntityRuleCommand(
            "CustomerTransactions", "CustomerDOB", "LessThanOrEqual", "Function",
            "2008-01-01", null, "TodayMinusYears", 18, null);

        var result = Validator().Validate(command);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.ErrorMessage.Contains("must be empty for function rules"));
    }

    [Fact]
    public void Validate_UnknownOperator_IsInvalid()
    {
        var result = Validator().Validate(Constant(op: "ApproxEquals"));

        result.IsValid.ShouldBeFalse();
    }
}
