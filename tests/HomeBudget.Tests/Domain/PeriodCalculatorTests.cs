using HomeBudget.Api.Domain.Periods;
using HomeBudget.Api.Domain.Settings;

namespace HomeBudget.Tests.Domain;

public sealed class PeriodCalculatorTests
{
    private static readonly PeriodCalculator Calendar = new(PeriodMode.Calendar, 25);

    [Theory]
    [InlineData("2026-01-01", "2026-01-01", "2026-01-31", "2026-01")]
    [InlineData("2026-01-31", "2026-01-01", "2026-01-31", "2026-01")]
    [InlineData("2026-02-15", "2026-02-01", "2026-02-28", "2026-02")]
    [InlineData("2028-02-29", "2028-02-01", "2028-02-29", "2028-02")]
    [InlineData("2026-12-31", "2026-12-01", "2026-12-31", "2026-12")]
    public void Calendar_period_is_the_month_whatever_the_start_day(string date, string start, string end, string key)
    {
        var period = Calendar.For(DateOnly.Parse(date));

        period.Start.ShouldBe(DateOnly.Parse(start));
        period.End.ShouldBe(DateOnly.Parse(end));
        period.Key.ShouldBe(key);
    }

    [Theory]
    [InlineData("2026-11-25", "2026-11-25", "2026-12-24", "2026-12")]
    [InlineData("2026-11-24", "2026-10-25", "2026-11-24", "2026-11")]
    [InlineData("2026-12-31", "2026-12-25", "2027-01-24", "2027-01")]
    [InlineData("2027-01-10", "2026-12-25", "2027-01-24", "2027-01")]
    [InlineData("2028-02-29", "2028-02-25", "2028-03-24", "2028-03")]
    public void Fixed_day_25_is_named_after_the_end_month(string date, string start, string end, string key)
    {
        var period = new PeriodCalculator(PeriodMode.FixedDay, 25).For(DateOnly.Parse(date));

        period.Start.ShouldBe(DateOnly.Parse(start));
        period.End.ShouldBe(DateOnly.Parse(end));
        period.Key.ShouldBe(key);
    }

    [Theory]
    [InlineData("2026-03-09", "2026-02-10", "2026-03-09", "2026-02")]
    [InlineData("2026-03-10", "2026-03-10", "2026-04-09", "2026-03")]
    public void Fixed_day_10_is_named_after_the_start_month(string date, string start, string end, string key)
    {
        var period = new PeriodCalculator(PeriodMode.FixedDay, 10).For(DateOnly.Parse(date));

        period.Start.ShouldBe(DateOnly.Parse(start));
        period.End.ShouldBe(DateOnly.Parse(end));
        period.Key.ShouldBe(key);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(15)]
    [InlineData(16)]
    [InlineData(25)]
    [InlineData(28)]
    public void Consecutive_periods_cover_every_day_and_every_month_once(int startDay)
    {
        var calculator = new PeriodCalculator(PeriodMode.FixedDay, startDay);
        var period = calculator.For(new DateOnly(2027, 11, 20));
        for (var i = 0; i < 60; i++)
        {
            var next = calculator.Next(period);
            next.Start.ShouldBe(period.End.AddDays(1));
            new DateOnly(next.Year, next.Month, 1).ShouldBe(new DateOnly(period.Year, period.Month, 1).AddMonths(1));
            calculator.Previous(next).ShouldBe(period);
            calculator.TryParse(next.Key, out var parsed).ShouldBeTrue();
            parsed.ShouldBe(next);
            period = next;
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("2026-13")]
    [InlineData("2026-00")]
    [InlineData("2026-1")]
    [InlineData("26-11-01")]
    [InlineData("abcd-ef")]
    [InlineData("+026-11")]
    public void Invalid_keys_are_rejected(string? key) =>
        Calendar.TryParse(key, out _).ShouldBeFalse();

    [Fact]
    public void Start_day_is_limited_to_28_in_fixed_day_mode()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new PeriodCalculator(PeriodMode.FixedDay, 29));
        Should.Throw<ArgumentOutOfRangeException>(() => new PeriodCalculator(PeriodMode.FixedDay, 0));
    }

    [Fact]
    public void Period_knows_its_length_and_contents()
    {
        var period = Calendar.For(new DateOnly(2028, 2, 10));

        period.Days.ShouldBe(29);
        period.Contains(new DateOnly(2028, 2, 29)).ShouldBeTrue();
        period.Contains(new DateOnly(2028, 3, 1)).ShouldBeFalse();
    }
}
