using HomeBudget.Api.Domain.Scenarios;

namespace HomeBudget.Tests.Domain;

public sealed class ScenarioEventTests
{
    private static ScenarioEvent Event(DateOnly date, ScenarioEventFrequency frequency, DateOnly? end = null) =>
        new() { Label = "Test", Amount = -10m, Date = date, Frequency = frequency, EndDate = end };

    [Fact]
    public void One_off_event_falls_once_inside_the_range()
    {
        var e = Event(new DateOnly(2026, 3, 15), ScenarioEventFrequency.Once);

        e.DatesBetween(new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31)).ShouldBe([new DateOnly(2026, 3, 15)]);
        e.DatesBetween(new DateOnly(2026, 3, 16), new DateOnly(2026, 12, 31)).ShouldBeEmpty();
    }

    [Fact]
    public void Monthly_event_is_computed_from_its_start_without_drift()
    {
        var e = Event(new DateOnly(2026, 1, 31), ScenarioEventFrequency.Monthly);

        e.DatesBetween(new DateOnly(2026, 2, 1), new DateOnly(2026, 4, 30))
            .ShouldBe([new DateOnly(2026, 2, 28), new DateOnly(2026, 3, 31), new DateOnly(2026, 4, 30)]);
    }

    [Fact]
    public void Monthly_event_stops_at_its_end_date()
    {
        var e = Event(new DateOnly(2026, 1, 10), ScenarioEventFrequency.Monthly, new DateOnly(2026, 3, 9));

        e.DatesBetween(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31))
            .ShouldBe([new DateOnly(2026, 1, 10), new DateOnly(2026, 2, 10)]);
    }
}
