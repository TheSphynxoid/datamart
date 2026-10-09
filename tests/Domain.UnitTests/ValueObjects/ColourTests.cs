using DataMart.Domain.Exceptions;
using DataMart.Domain.ValueObjects;
using Shouldly;
using Xunit;

namespace DataMart.Domain.UnitTests.ValueObjects;

public class ColourTests
{
    [Fact]
    public void ShouldReturnCorrectColourCode()
    {
        var code = "#E05C4D";

        var colour = Colour.From(code);

        colour.Code.ShouldBe(code);
    }

    [Fact]
    public void ToStringReturnsCode()
    {
        var colour = Colour.Red;

        colour.ToString().ShouldBe(colour.Code);
    }

    [Fact]
    public void ShouldPerformImplicitConversionToColourCodeString()
    {
        string code = Colour.Red;

        code.ShouldBe("#E05C4D");
    }

    [Fact]
    public void ShouldPerformExplicitConversionGivenSupportedColourCode()
    {
        var colour = (Colour)"#E05C4D";

        colour.ShouldBe(Colour.Red);
    }

    [Fact]
    public void ShouldThrowUnsupportedColourExceptionGivenNotSupportedColourCode()
    {
        Should.Throw<UnsupportedColourException>(() => Colour.From("##FF33CC"));
    }

    [Fact]
    public void ShouldBeComparableWithOperators()
    {
        var color1 = new Colour("#E05C4D");
        var color2 = new Colour("#E05C4D");
        var color3 = new Colour("#AAAAAA");
        (color1 == color2).ShouldBe(true);
        (color1 == color3).ShouldBe(false);
    }
}
