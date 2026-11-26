using HomeBudget.Api.Domain.Periods;
using HomeBudget.Api.Domain.Savings;
using HomeBudget.Api.Domain.Settings;

namespace HomeBudget.Tests.Domain;

public sealed class SavingsGoalPlanTests
{
    private static readonly PeriodCalculator Calendar = new(PeriodMode.Calendar, 1);
    private static readonly DateOnly Today = new(2026, 3, 10);

    [Fact]
    public void Monthly_contribution_spreads_the_remainder_over_the_periods_left_including_the_current_one()
    {
        // De mars à août inclus : 6 périodes pour 1 000 € restants, arrondi au centime supérieur.
        var progress = SavingsGoalPlan.Compute(1500m, 500m, new DateOnly(2026, 8, 15), Today, Calendar);

        progress.Remaining.ShouldBe(1000m);
        progress.Percent.ShouldBe(33);
        progress.Status.ShouldBe(SavingsGoalStatus.InProgress);
        progress.RemainingPeriods.ShouldBe(6);
        progress.MonthlyContribution.ShouldBe(166.67m);
    }

    [Fact]
    public void Target_in_the_current_period_asks_for_the_whole_remainder()
    {
        var progress = SavingsGoalPlan.Compute(1000m, 400m, new DateOnly(2026, 3, 31), Today, Calendar);

        progress.RemainingPeriods.ShouldBe(1);
        progress.MonthlyContribution.ShouldBe(600m);
    }

    [Fact]
    public void Periods_follow_a_fixed_start_day()
    {
        // Périodes du 25 au 24 : le 10/03 est dans la période du 25/02, le 30/04 dans celle du 25/04.
        var fixedDay = new PeriodCalculator(PeriodMode.FixedDay, 25);

        SavingsGoalPlan.Compute(300m, 0m, new DateOnly(2026, 4, 30), Today, fixedDay).RemainingPeriods.ShouldBe(3);
    }

    [Fact]
    public void Reached_goal_needs_no_more_contribution()
    {
        var progress = SavingsGoalPlan.Compute(1000m, 1250m, new DateOnly(2026, 12, 31), Today, Calendar);

        progress.Status.ShouldBe(SavingsGoalStatus.Reached);
        progress.Remaining.ShouldBe(0m);
        progress.Percent.ShouldBe(100);
        progress.MonthlyContribution.ShouldBe(0m);
    }

    [Fact]
    public void Past_target_date_makes_the_goal_overdue_without_advice()
    {
        var progress = SavingsGoalPlan.Compute(1000m, 200m, new DateOnly(2026, 2, 28), Today, Calendar);

        progress.Status.ShouldBe(SavingsGoalStatus.Overdue);
        progress.RemainingPeriods.ShouldBe(0);
        progress.MonthlyContribution.ShouldBeNull();
    }

    [Fact]
    public void Without_target_date_there_is_no_advice_and_a_negative_saving_counts_as_zero_percent()
    {
        var progress = SavingsGoalPlan.Compute(1000m, -50m, null, Today, Calendar);

        progress.Status.ShouldBe(SavingsGoalStatus.InProgress);
        progress.Percent.ShouldBe(0);
        progress.Remaining.ShouldBe(1050m);
        progress.RemainingPeriods.ShouldBeNull();
        progress.MonthlyContribution.ShouldBeNull();
    }

    [Fact]
    public void Emergency_fund_counts_the_months_of_expenses_covered()
    {
        EmergencyFund.MonthsCovered(10000m, 2400m).ShouldBe(4.1m);
        EmergencyFund.MonthsCovered(-100m, 2400m).ShouldBe(0m);
        EmergencyFund.MonthsCovered(10000m, 0m).ShouldBeNull();
    }
}
