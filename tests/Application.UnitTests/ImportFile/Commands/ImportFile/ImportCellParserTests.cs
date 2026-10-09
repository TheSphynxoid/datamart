using DataMart.Application.Common.Exceptions;
using DataMart.Application.Common.Interfaces;
using DataMart.Application.ImportFile.Common;
using DataMart.Domain.Entities;
using Shouldly;
using Xunit;

namespace DataMart.Application.UnitTests.ImportFile.Commands.ImportFile;

/// <summary>
/// Edge-case coverage for the shared cell-parsing primitives. These run the same
/// rules as both validation and import, so a regression here breaks both paths.
/// </summary>
public class ImportCellParserTests
{
    [Theory]
    [InlineData("nan")]
    [InlineData("NAN")]
    [InlineData("N/A")]
    [InlineData("n/a")]
    [InlineData("NA")]
    [InlineData("-")]
    [InlineData("")]
    [InlineData("   ")]
    public void IsNullish_PlaceholderValues_ReturnsTrue(string value)
    {
        ImportCellParser.IsNullish(value).ShouldBeTrue();
    }

    [Theory]
    [InlineData("valid")]
    [InlineData("0")]
    [InlineData("12.50")]
    public void IsNullish_RealValues_ReturnsFalse(string value)
    {
        ImportCellParser.IsNullish(value).ShouldBeFalse();
    }

    [Theory]
    [InlineData("String")]
    [InlineData("Guid")]
    [InlineData("SomethingElse")]
    public void MapType_UnknownClr_ReturnsText(string clr)
    {
        ImportCellParser.MapType(clr).ShouldBe(ImportColumnType.Text);
    }

    [Fact]
    public void Parse_NullCell_ReturnsNull()
    {
        var mapping = new ColumnMapping();

        ImportCellParser.Parse(null, mapping, ImportColumnType.Decimal).ShouldBeNull();
    }

    [Fact]
    public void ParseDecimal_InvalidString_ThrowsValidationException()
    {
        Should.Throw<AppValidationException>(
            () => ImportCellParser.ParseDecimal("abc", ".", stripSymbols: false));
    }

    [Fact]
    public void ParseDate_InvalidString_ThrowsValidationException()
    {
        Should.Throw<AppValidationException>(
            () => ImportCellParser.ParseDate("not-a-date", null));
    }

    [Fact]
    public void FromExcelSerial_OutOfRange_ThrowsValidationException()
    {
        Should.Throw<AppValidationException>(() => ImportCellParser.FromExcelSerial(-5));
        Should.Throw<AppValidationException>(() => ImportCellParser.FromExcelSerial(100_000));
    }
}
