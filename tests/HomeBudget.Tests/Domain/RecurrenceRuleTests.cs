using HomeBudget.Api.Domain.Recurrences;

namespace HomeBudget.Tests.Domain;

public sealed class RecurrenceRuleTests
{
    private static DateOnly D(int year, int month, int day) => new(year, month, day);

    private static List<DateOnly> Dates(RecurrenceRule rule, DateOnly from, DateOnly to) =>
        [.. rule.Between(from, to).Select(s => s.Date)];

    [Fact]
    public void Monthly_from_the_31st_does_not_drift()
    {
        var rule = new RecurrenceRule(D(2026, 1, 31), FrequencyUnit.Month, 1);

        Dates(rule, D(2026, 1, 1), D(2026, 5, 31)).ShouldBe(
            [D(2026, 1, 31), D(2026, 2, 28), D(2026, 3, 31), D(2026, 4, 30), D(2026, 5, 31)]);
    }

    [Fact]
    public void Leap_years_are_respected()
    {
        new RecurrenceRule(D(2028, 1, 31), FrequencyUnit.Month, 1).DateOf(1).ShouldBe(D(2028, 2, 29));
        var yearly = new RecurrenceRule(D(2024, 2, 29), FrequencyUnit.Year, 1);
        yearly.DateOf(1).ShouldBe(D(2025, 2, 28));
        yearly.DateOf(4).ShouldBe(D(2028, 2, 29));
    }

    [Theory]
    [InlineData(FrequencyUnit.Day, 3, "2026-03-07")]
    [InlineData(FrequencyUnit.Week, 2, "2026-03-29")]
    [InlineData(FrequencyUnit.Month, 3, "2026-09-01")]
    [InlineData(FrequencyUnit.Year, 1, "2028-03-01")]
    public void Every_n_units(FrequencyUnit unit, int interval, string third)
    {
        new RecurrenceRule(D(2026, 3, 1), unit, interval).DateOf(2).ShouldBe(DateOnly.Parse(third, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Ends_after_count_or_on_date()
    {
        var count = new RecurrenceRule(D(2026, 1, 10), FrequencyUnit.Month, 1, RecurrenceEnd.AfterCount, Count: 3);
        Dates(count, D(2025, 1, 1), D(2027, 1, 1)).ShouldBe([D(2026, 1, 10), D(2026, 2, 10), D(2026, 3, 10)]);

        var until = new RecurrenceRule(D(2026, 1, 10), FrequencyUnit.Month, 1, RecurrenceEnd.OnDate, EndDate: D(2026, 3, 9));
        Dates(until, D(2025, 1, 1), D(2027, 1, 1)).ShouldBe([D(2026, 1, 10), D(2026, 2, 10)]);
    }

    [Fact]
    public void Between_returns_indexes_counted_from_the_start()
    {
        var rule = new RecurrenceRule(D(2020, 1, 15), FrequencyUnit.Week, 1);

        var window = rule.Between(D(2026, 11, 1), D(2026, 11, 30)).ToList();

        window.Count.ShouldBe(4);
        window.ShouldAllBe(s => rule.DateOf(s.Index) == s.Date);
        window[0].Date.ShouldBeGreaterThanOrEqualTo(D(2026, 11, 1));
        rule.Between(D(2026, 1, 1), D(2019, 1, 1)).ShouldBeEmpty();
        rule.Between(D(2010, 1, 1), D(2019, 1, 1)).ShouldBeEmpty();
    }

    [Fact]
    public void Validation_reports_each_field()
    {
        new RecurrenceRule(D(2026, 1, 1), FrequencyUnit.Month, 1).Validate().ShouldBeEmpty();
        new RecurrenceRule(D(2026, 1, 1), (FrequencyUnit)42, 0).Validate().Keys.ShouldBe(["unit", "interval"], ignoreOrder: true);
        new RecurrenceRule(D(2026, 1, 1), FrequencyUnit.Month, 1, RecurrenceEnd.OnDate, D(2025, 1, 1)).Validate().Keys.ShouldBe(["endDate"]);
        new RecurrenceRule(D(2026, 1, 1), FrequencyUnit.Month, 1, RecurrenceEnd.AfterCount, Count: 0).Validate().Keys.ShouldBe(["count"]);
        new RecurrenceRule(D(2026, 1, 1), FrequencyUnit.Month, 1, (RecurrenceEnd)9).Validate().Keys.ShouldBe(["end"]);
    }

    [Fact]
    public void Due_occurrences_skip_the_past_and_already_processed_dates()
    {
        var rule = new RecurrenceRule(D(2026, 1, 5), FrequencyUnit.Month, 1);
        OccurrenceState[] processed = [new(D(2026, 3, 5), OccurrenceStatus.Generated, D(2026, 3, 5))];

        var due = RecurrenceSchedule.DueOn(rule, generateFrom: D(2026, 3, 1), processed, today: D(2026, 5, 5));

        due.Select(s => s.Date).ShouldBe([D(2026, 4, 5), D(2026, 5, 5)]);
        due[0].Index.ShouldBe(3);
    }

    [Fact]
    public void Remaining_includes_pending_and_unprocessed_but_not_skipped_or_generated()
    {
        var rule = new RecurrenceRule(D(2026, 1, 5), FrequencyUnit.Month, 1);
        OccurrenceState[] states =
        [
            new(D(2026, 1, 5), OccurrenceStatus.Generated, D(2026, 1, 5)),
            new(D(2026, 2, 5), OccurrenceStatus.Pending, D(2026, 2, 5)),
            new(D(2026, 3, 5), OccurrenceStatus.Pending, D(2026, 6, 20)), // reportée au-delà de l'horizon
            new(D(2026, 4, 5), OccurrenceStatus.Skipped, D(2026, 4, 5)),
        ];

        var remaining = RecurrenceSchedule.Remaining(rule, D(2026, 1, 1), states, until: D(2026, 5, 31));

        remaining.ShouldBe([
            new ExpectedOccurrence(1, D(2026, 2, 5), D(2026, 2, 5)),
            new ExpectedOccurrence(4, D(2026, 5, 5), D(2026, 5, 5)),
        ]);
    }

    [Fact]
    public void Planned_occurrences_exclude_skipped_ones()
    {
        var rule = new RecurrenceRule(D(2026, 1, 1), FrequencyUnit.Week, 1);
        OccurrenceState[] states = [new(D(2026, 1, 8), OccurrenceStatus.Skipped, D(2026, 1, 8))];

        RecurrenceSchedule.Planned(rule, states, D(2026, 1, 1), D(2026, 1, 31)).Count.ShouldBe(4);
    }

    [Fact]
    public void Installment_labels_are_numbered()
    {
        RecurrenceSchedule.Label("Montre", isInstallment: true, index: 0, count: 4).ShouldBe("Montre 1/4");
        RecurrenceSchedule.Label("Montre", isInstallment: true, index: 3, count: 4).ShouldBe("Montre 4/4");
        RecurrenceSchedule.Label("Loyer", isInstallment: false, index: 3, count: null).ShouldBe("Loyer");
    }
}
