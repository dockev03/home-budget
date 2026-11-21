using HomeBudget.Api.Infrastructure.Data;

namespace HomeBudget.Tests.Domain;

public sealed class CentsConverterTests
{
    [Theory]
    [InlineData("0", 0)]
    [InlineData("12.34", 1234)]
    [InlineData("-97.00", -9700)]
    [InlineData("0.005", 1)]
    [InlineData("-0.005", -1)]
    [InlineData("2725.5", 272550)]
    public void Converts_to_cents(string value, long expected) =>
        CentsConverter.ToCents(decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture)).ShouldBe(expected);

    [Fact]
    public void Round_trips() =>
        CentsConverter.FromCents(CentsConverter.ToCents(-1234.56m)).ShouldBe(-1234.56m);
}
