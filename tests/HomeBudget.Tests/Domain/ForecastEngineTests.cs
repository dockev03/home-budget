using HomeBudget.Api.Domain.Categories;
using HomeBudget.Api.Domain.Forecasts;
using HomeBudget.Api.Domain.Periods;

namespace HomeBudget.Tests.Domain;

public sealed class ForecastEngineTests
{
    private static readonly Period March = new(new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31), 2026, 3);

    private static readonly Guid Salary = Guid.NewGuid();
    private static readonly Guid Housing = Guid.NewGuid();
    private static readonly Guid Rent = Guid.NewGuid();
    private static readonly Guid Internet = Guid.NewGuid();
    private static readonly Guid Groceries = Guid.NewGuid();
    private static readonly Guid Livret = Guid.NewGuid();

    private static readonly ForecastCategory[] Categories =
    [
        new(Salary, null, CategoryKind.Income),
        new(Housing, null, CategoryKind.Fixed),
        new(Rent, Housing, CategoryKind.Fixed),
        new(Internet, null, CategoryKind.Fixed),
        new(Groceries, null, CategoryKind.Variable),
        new(Livret, null, CategoryKind.Savings),
    ];

    private static DateOnly D(int day) => new(2026, 3, day);

    /// <summary>
    /// Au 10 mars : 1 000 € au 1er, 100 € de courses le 2, 50 € à venir le 15 (sans catégorie), loyer de 600 € le 20,
    /// box de 30 € à valider depuis le 5. Budgets restants : logement 700, box 30, courses 300, salaire 2 000.
    /// </summary>
    private static ForecastInput Input(DateOnly today, bool includeIncome = true, decimal threshold = 0m, decimal safety = 0m) => new(
        March,
        today,
        OpeningBalance: 1000m,
        Transactions: [new(D(2), -100m, Groceries), new(D(15), -50m, null)],
        Scheduled: [new(D(5), -30m, Internet), new(D(20), -600m, Rent)],
        ScheduledByCategory: new Dictionary<Guid, decimal> { [Rent] = -600m, [Internet] = -30m },
        Categories,
        Budgets: [new(Housing, 700m), new(Internet, 30m), new(Groceries, 300m), new(Salary, 2000m)],
        includeIncome,
        threshold,
        safety);

    [Fact]
    public void End_balance_adds_future_transactions_schedules_and_uncovered_budget()
    {
        var result = ForecastEngine.Compute(Input(D(10)));

        result.CurrentBalance.ShouldBe(900m);
        result.FutureTransactions.ShouldBe(-50m);
        result.Scheduled.ShouldBe(-630m);
        // Logement : 700 − 600 de loyer prévu = 100 ; box : couverte ; courses : 300 ; salaire : + 2 000.
        result.Budget.ShouldBe(1600m);
        result.EndBalance.ShouldBe(900m - 50m - 630m + 1600m);
        result.ThresholdDate.ShouldBeNull();
    }

    [Fact]
    public void Daily_curve_is_real_until_today_then_forecast()
    {
        var result = ForecastEngine.Compute(Input(D(10)));

        result.Daily.Count.ShouldBe(31);
        result.Daily[0].ShouldBe(new ForecastPoint(D(1), 1000m, false));
        result.Daily[1].ShouldBe(new ForecastPoint(D(2), 900m, false));
        result.Daily[9].ShouldBe(new ForecastPoint(D(10), 900m, false));
        // Le 11 : la box en retard, et 1 600 / 21 jours de budget (arrondi au centime inférieur).
        result.Daily[10].ShouldBe(new ForecastPoint(D(11), 900m - 30m + 76.19m, true));
        result.Daily[^1].Balance.ShouldBe(result.EndBalance);
        result.Daily.Skip(10).ShouldAllBe(p => p.IsForecast);
    }

    [Fact]
    public void Remaining_to_live_counts_income_fixed_and_savings_only()
    {
        var result = ForecastEngine.Compute(Input(D(10), safety: 20m));

        result.Expected.ShouldBe(new ExpectedByKind(Income: 2000m, Fixed: 50m + 630m + 100m, Variable: 300m, Savings: 0m));
        var rav = result.RemainingToLive;
        rav.Total.ShouldBe(900m + 2000m - 780m - 20m);
        rav.DaysLeft.ShouldBe(22);
        rav.PerDay.ShouldBe(95.45m);
        rav.SafetyThreshold.ShouldBe(20m);
    }

    [Fact]
    public void Excluding_income_reveals_the_threshold_crossing()
    {
        var result = ForecastEngine.Compute(Input(D(10), includeIncome: false));

        result.Budget.ShouldBe(-400m);
        result.EndBalance.ShouldBe(900m - 50m - 630m - 400m);
        // 900 − 30 − 50 − 600 − 10 × 19,04 = 29,60 le 20 ; 10,56 le 21 ; négatif le 22.
        result.ThresholdDate.ShouldBe(D(22));
        result.RemainingToLive.Income.ShouldBe(0m);
    }

    [Fact]
    public void Threshold_already_crossed_is_reported_today()
    {
        ForecastEngine.Compute(Input(D(10), threshold: 1000m)).ThresholdDate.ShouldBe(D(10));
    }

    [Fact]
    public void On_the_last_day_only_overdue_schedules_remain()
    {
        var input = Input(D(31)) with { Scheduled = [new(D(5), -30m, Internet)] };

        var result = ForecastEngine.Compute(input);

        result.CurrentBalance.ShouldBe(850m);
        result.Budget.ShouldBe(0m);
        result.EndBalance.ShouldBe(820m);
        result.Daily.ShouldAllBe(p => !p.IsForecast);
        result.RemainingToLive.DaysLeft.ShouldBe(1);
        result.RemainingToLive.Fixed.ShouldBe(30m);
    }

    [Fact]
    public void Transfers_between_forecast_accounts_cancel_out_and_savings_are_counted()
    {
        var input = Input(D(10)) with
        {
            Transactions = [],
            Scheduled = [new(D(12), -200m, Livret), new(D(25), -80m, null), new(D(25), 80m, null)],
            ScheduledByCategory = new Dictionary<Guid, decimal> { [Livret] = -200m },
            Budgets = [new(Livret, 250m)],
        };

        var result = ForecastEngine.Compute(input);

        result.Budget.ShouldBe(-50m);
        result.EndBalance.ShouldBe(1000m - 200m - 50m);
        result.Expected.Savings.ShouldBe(250m);
        result.Expected.Income.ShouldBe(80m);
        result.Expected.Fixed.ShouldBe(80m);
        result.RemainingToLive.Total.ShouldBe(1000m - 250m);
    }

    [Fact]
    public void Overspent_budget_adds_nothing_and_unknown_categories_are_ignored()
    {
        var input = Input(D(10)) with
        {
            Scheduled = [],
            ScheduledByCategory = new Dictionary<Guid, decimal>(),
            Budgets = [new(Groceries, -40m), new(Guid.NewGuid(), 500m)],
        };

        ForecastEngine.Compute(input).Budget.ShouldBe(0m);
    }

    private static readonly Period April = new(new DateOnly(2026, 4, 1), new DateOnly(2026, 4, 30), 2026, 4);

    private static DateOnly A(int day) => new(2026, 4, day);

    /// <summary>
    /// Avril : 100 € de courses déjà saisies le 5, loyer de 600 € le 20. Restes : logement 700, courses 300
    /// (400 − 100 saisis), salaire 2 000.
    /// </summary>
    private static ForecastFuturePeriod AprilInput() => new(
        April,
        Transactions: [new(A(5), -100m, Groceries)],
        Scheduled: [new(A(20), -600m, Rent)],
        ScheduledByCategory: new Dictionary<Guid, decimal> { [Rent] = -600m },
        Budgets: [new(Housing, 700m), new(Groceries, 300m), new(Salary, 2000m)]);

    [Fact]
    public void Next_periods_start_from_the_previous_end_and_spread_their_whole_budget()
    {
        var march = ForecastEngine.Compute(Input(D(10)));
        var result = ForecastEngine.Compute(Input(D(10)) with { FuturePeriods = [AprilInput()] });

        // La période en cours ne change pas.
        result.EndBalance.ShouldBe(march.EndBalance);
        result.RemainingToLive.ShouldBe(march.RemainingToLive);
        result.Daily.Count.ShouldBe(31 + 30);
        // Avril : − 100 − 600 + (100 de logement non couvert + 300 de courses, en sortie ; 2 000 de salaire) = + 900.
        result.Periods.Select(p => p.Period).ShouldBe([March, April]);
        result.Periods[0].EndBalance.ShouldBe(march.EndBalance);
        result.Periods[1].EndBalance.ShouldBe(march.EndBalance + 900m);
        result.Daily[^1].ShouldBe(new ForecastPoint(A(30), march.EndBalance + 900m, true));
        // Le 1er avril : 1 600 / 30 jours de budget, arrondi au centime inférieur.
        result.Daily[31].ShouldBe(new ForecastPoint(A(1), march.EndBalance + 53.33m, true));
        result.Periods.ShouldAllBe(p => p.ScenarioEndBalance == null);
        result.Scenario.ShouldBeNull();
    }

    [Fact]
    public void Threshold_date_is_searched_over_the_whole_horizon()
    {
        var april = AprilInput() with { Budgets = [new(Groceries, 5000m)] };
        var result = ForecastEngine.Compute(Input(D(10)) with { FuturePeriods = [april] });

        result.Periods[0].EndBalance.ShouldBeGreaterThan(0m);
        result.ThresholdDate.ShouldNotBeNull();
        result.ThresholdDate!.Value.Month.ShouldBe(4);
        result.Daily.First(p => p.Balance < 0).Date.ShouldBe(result.ThresholdDate.Value);
    }

    [Fact]
    public void On_the_last_day_overdue_schedules_carry_over_to_the_next_period()
    {
        var input = Input(D(31)) with { FuturePeriods = [AprilInput() with { Transactions = [], Scheduled = [], Budgets = [] }] };

        var result = ForecastEngine.Compute(input);

        // La box à valider en retard (30 €) et le loyer (600 €) comptent dans le solde de fin de mars.
        result.EndBalance.ShouldBe(result.CurrentBalance - 630m);
        result.Daily[31].Balance.ShouldBe(result.EndBalance);
        result.Periods[1].EndBalance.ShouldBe(result.EndBalance);
    }

    [Fact]
    public void Scenario_is_a_separate_series_from_today_to_the_end_of_the_horizon()
    {
        var input = Input(D(10)) with
        {
            FuturePeriods = [AprilInput()],
            ScenarioMovements =
            [
                new(D(5), -999m, null), // passé : ignoré
                new(D(20), -500m, null),
                new(A(10), -300m, Groceries),
                new(A(10), 100m, null),
                new(new DateOnly(2026, 5, 1), -999m, null), // après l'horizon : ignoré
            ],
            Threshold = 2000m,
        };
        var baseline = ForecastEngine.Compute(input with { ScenarioMovements = null });

        var result = ForecastEngine.Compute(input);

        // La prévision de base reste intacte.
        result.Daily.ShouldBe(baseline.Daily);
        result.EndBalance.ShouldBe(baseline.EndBalance);
        result.Periods.Select(p => p.EndBalance).ShouldBe(baseline.Periods.Select(p => p.EndBalance));

        var scenario = result.Scenario.ShouldNotBeNull();
        scenario.Impact.ShouldBe(-700m);
        scenario.Daily[0].ShouldBe(new ForecastPoint(D(10), 900m, false));
        scenario.Daily.Count.ShouldBe(22 + 30);
        scenario.Daily.Single(p => p.Date == D(19)).Balance.ShouldBe(baseline.Daily.Single(p => p.Date == D(19)).Balance);
        scenario.Daily.Single(p => p.Date == D(20)).Balance.ShouldBe(baseline.Daily.Single(p => p.Date == D(20)).Balance - 500m);
        scenario.Daily.Single(p => p.Date == A(10)).Balance.ShouldBe(baseline.Daily.Single(p => p.Date == A(10)).Balance - 700m);
        scenario.EndBalance.ShouldBe(baseline.Periods[1].EndBalance - 700m);
        result.Periods[0].ScenarioEndBalance.ShouldBe(baseline.Periods[0].EndBalance - 500m);
        result.Periods[1].ScenarioEndBalance.ShouldBe(scenario.EndBalance);
        scenario.ThresholdDate.ShouldBe(scenario.Daily.First(p => p.Balance < 2000m).Date);
    }

    [Fact]
    public void Empty_scenario_gives_the_baseline()
    {
        var result = ForecastEngine.Compute(Input(D(10)) with { ScenarioMovements = [] });

        var scenario = result.Scenario.ShouldNotBeNull();
        scenario.Impact.ShouldBe(0m);
        scenario.EndBalance.ShouldBe(result.EndBalance);
        scenario.ThresholdDate.ShouldBe(result.ThresholdDate);
        result.Periods[0].ScenarioEndBalance.ShouldBe(result.EndBalance);
    }

    [Fact]
    public void Today_must_be_in_the_period()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => ForecastEngine.Compute(Input(new DateOnly(2026, 4, 1))));
    }
}
