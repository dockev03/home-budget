using HomeBudget.Api.Domain.Budgets;
using HomeBudget.Api.Domain.Periods;
using HomeBudget.Api.Domain.Settings;

namespace HomeBudget.Tests.Domain;

public sealed class ProvisionPlanTests
{
    private static readonly PeriodCalculator Calendar = new(PeriodMode.Calendar, 1);

    private static Period P(int year, int month) => Calendar.For(new DateOnly(year, month, 1));

    [Fact]
    public void One_off_provision_is_spread_up_to_its_due_period_and_the_last_share_takes_the_rounding()
    {
        // 1 000 € dus en mars, lissés depuis janvier : 333,33 + 333,33 + 333,34.
        var january = ProvisionPlan.For(1000m, P(2026, 1), P(2026, 3), isAnnual: false, P(2026, 1))!.Value;
        january.Share.ShouldBe(333.33m);
        january.IsDue.ShouldBeFalse();
        january.Saved.ShouldBe(333.33m);
        january.Periods.ShouldBe(3);
        january.DueKey.ShouldBe("2026-03");

        var march = ProvisionPlan.For(1000m, P(2026, 1), P(2026, 3), isAnnual: false, P(2026, 3))!.Value;
        march.Share.ShouldBe(333.34m);
        march.IsDue.ShouldBeTrue();
        march.Saved.ShouldBe(1000m);

        ProvisionPlan.For(1000m, P(2026, 1), P(2026, 3), isAnnual: false, P(2025, 12)).ShouldBeNull();
        ProvisionPlan.For(1000m, P(2026, 1), P(2026, 3), isAnnual: false, P(2026, 4)).ShouldBeNull();
    }

    [Fact]
    public void Annual_provision_restarts_each_year_over_twelve_periods()
    {
        // Taxe foncière de 1 200 € en octobre, lissage commencé en janvier 2026 : premier cycle de 10 périodes.
        var first = ProvisionPlan.For(1200m, P(2026, 1), P(2026, 10), isAnnual: true, P(2026, 5))!.Value;
        first.Periods.ShouldBe(10);
        first.Share.ShouldBe(120m);
        first.Saved.ShouldBe(600m);

        var second = ProvisionPlan.For(1200m, P(2026, 1), P(2026, 10), isAnnual: true, P(2026, 11))!.Value;
        second.Periods.ShouldBe(12);
        second.Share.ShouldBe(100m);
        second.DueKey.ShouldBe("2027-10");
        second.Saved.ShouldBe(100m);

        var due = ProvisionPlan.For(1200m, P(2026, 1), P(2026, 10), isAnnual: true, P(2027, 10))!.Value;
        due.IsDue.ShouldBeTrue();
        due.Saved.ShouldBe(1200m);
    }
}
