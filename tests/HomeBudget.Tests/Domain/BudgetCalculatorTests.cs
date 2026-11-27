using HomeBudget.Api.Domain.Budgets;
using HomeBudget.Api.Domain.Categories;

namespace HomeBudget.Tests.Domain;

public sealed class BudgetCalculatorTests
{
    private static readonly Guid Salary = Guid.NewGuid();
    private static readonly Guid Bonus = Guid.NewGuid();
    private static readonly Guid Rent = Guid.NewGuid();
    private static readonly Guid Internet = Guid.NewGuid();
    private static readonly Guid Groceries = Guid.NewGuid();
    private static readonly Guid Drive = Guid.NewGuid();
    private static readonly Guid Fuel = Guid.NewGuid();
    private static readonly Guid Livret = Guid.NewGuid();

    private static readonly BudgetThresholds Thresholds = new(80, 100);

    private static BudgetResult Compute(
        IReadOnlyList<BudgetCategory> categories,
        Dictionary<Guid, decimal>? actual = null,
        Dictionary<Guid, decimal>? scheduled = null) =>
        BudgetCalculator.Compute(new BudgetInput(categories, actual ?? [], scheduled ?? [], Thresholds));

    [Fact]
    public void Amount_mode_compares_budget_and_actual_with_expense_sign_reversed()
    {
        var result = Compute(
            [new(Groceries, null, CategoryKind.Variable, BudgetMode.Amount, 600m)],
            actual: new() { [Groceries] = -450m });

        var line = result.Categories[Groceries];
        line.Budgeted.ShouldBe(600m);
        line.Actual.ShouldBe(450m);
        line.Remaining.ShouldBe(150m);
        line.PercentUsed.ShouldBe(75m);
        line.Status.ShouldBe(BudgetStatus.Ok);
    }

    [Fact]
    public void A_refund_in_the_same_category_reduces_the_expense()
    {
        var result = Compute(
            [new(Groceries, null, CategoryKind.Variable, BudgetMode.Amount, 100m)],
            actual: new() { [Groceries] = -120m + 30m });

        result.Categories[Groceries].Actual.ShouldBe(90m);
    }

    [Fact]
    public void Percent_mode_uses_budgeted_income_including_auto_income()
    {
        var result = Compute(
            [
                new(Salary, null, CategoryKind.Income, BudgetMode.Amount, 3000m),
                new(Bonus, null, CategoryKind.Income, BudgetMode.Auto, 0m),
                new(Groceries, null, CategoryKind.Variable, BudgetMode.PercentOfIncome, 12.5m),
                new(Livret, null, CategoryKind.Savings, BudgetMode.PercentOfIncome, 10m),
            ],
            actual: new() { [Salary] = 2900m }, // le réel ne compte pas dans la base
            scheduled: new() { [Bonus] = 200m });

        result.IncomeBase.ShouldBe(3200m);
        result.Categories[Groceries].Budgeted.ShouldBe(400m);
        result.Categories[Livret].Budgeted.ShouldBe(320m);
    }

    [Fact]
    public void Percent_amounts_are_rounded_to_the_cent()
    {
        var result = Compute(
            [
                new(Salary, null, CategoryKind.Income, BudgetMode.Amount, 1234.56m),
                new(Fuel, null, CategoryKind.Variable, BudgetMode.PercentOfIncome, 3.33m),
            ]);

        result.Categories[Fuel].Budgeted.ShouldBe(41.11m); // 41,110848
    }

    [Fact]
    public void Fixed_expense_without_budget_defaults_to_auto_from_the_schedule()
    {
        var result = Compute(
            [
                new(Rent, null, CategoryKind.Fixed, null, 0m),
                new(Groceries, null, CategoryKind.Variable, null, 0m),
            ],
            scheduled: new() { [Rent] = -850m });

        var rent = result.Categories[Rent];
        rent.Mode.ShouldBe(BudgetMode.Auto);
        rent.IsDefaultMode.ShouldBeTrue();
        rent.Budgeted.ShouldBe(850m);

        var groceries = result.Categories[Groceries];
        groceries.Mode.ShouldBeNull();
        groceries.Budgeted.ShouldBe(0m);
        groceries.Status.ShouldBe(BudgetStatus.None);
    }

    [Fact]
    public void Auto_mode_without_schedule_is_zero()
    {
        var result = Compute([new(Internet, null, CategoryKind.Fixed, BudgetMode.Auto, 0m)]);

        result.Categories[Internet].Budgeted.ShouldBe(0m);
        result.Categories[Internet].IsDefaultMode.ShouldBeFalse();
    }

    [Fact]
    public void Parent_cumulates_its_own_budget_and_its_subcategories()
    {
        var result = Compute(
            [
                new(Groceries, null, CategoryKind.Variable, BudgetMode.Amount, 400m),
                new(Drive, Groceries, CategoryKind.Variable, BudgetMode.Amount, 100m),
            ],
            actual: new() { [Groceries] = -300m, [Drive] = -150m });

        var parent = result.Categories[Groceries];
        parent.OwnBudgeted.ShouldBe(400m);
        parent.Budgeted.ShouldBe(500m);
        parent.Actual.ShouldBe(450m);
        parent.Status.ShouldBe(BudgetStatus.Warning);
        result.Categories[Drive].Status.ShouldBe(BudgetStatus.Exceeded);

        result.Budgeted.Variable.ShouldBe(500m); // pas de double comptage
        result.Actual.Variable.ShouldBe(450m);
    }

    [Theory]
    [InlineData(-79.99, BudgetStatus.Ok)]
    [InlineData(-80, BudgetStatus.Warning)]
    [InlineData(-99.99, BudgetStatus.Warning)]
    [InlineData(-100, BudgetStatus.Exceeded)]
    [InlineData(-130, BudgetStatus.Exceeded)]
    public void Gauge_status_follows_thresholds(double actual, BudgetStatus expected)
    {
        var result = Compute(
            [new(Fuel, null, CategoryKind.Variable, BudgetMode.Amount, 100m)],
            actual: new() { [Fuel] = (decimal)actual });

        result.Categories[Fuel].Status.ShouldBe(expected);
    }

    [Fact]
    public void Spending_without_budget_is_exceeded_but_income_and_savings_have_no_gauge()
    {
        var result = Compute(
            [
                new(Fuel, null, CategoryKind.Variable, null, 0m),
                new(Salary, null, CategoryKind.Income, BudgetMode.Amount, 100m),
                new(Livret, null, CategoryKind.Savings, BudgetMode.Amount, 100m),
            ],
            actual: new() { [Fuel] = -10m, [Salary] = 500m, [Livret] = -500m });

        result.Categories[Fuel].Status.ShouldBe(BudgetStatus.Exceeded);
        result.Categories[Fuel].PercentUsed.ShouldBeNull();
        result.Categories[Salary].Status.ShouldBe(BudgetStatus.None);
        result.Categories[Livret].Status.ShouldBe(BudgetStatus.None);
    }

    [Fact]
    public void Summary_remaining_is_income_minus_fixed_variable_and_savings()
    {
        var result = Compute(
            [
                new(Salary, null, CategoryKind.Income, BudgetMode.Amount, 3000m),
                new(Rent, null, CategoryKind.Fixed, BudgetMode.Amount, 900m),
                new(Groceries, null, CategoryKind.Variable, BudgetMode.Amount, 600m),
                new(Livret, null, CategoryKind.Savings, BudgetMode.Amount, 300m),
            ],
            actual: new() { [Salary] = 3100m, [Rent] = -900m, [Groceries] = -640m, [Livret] = -300m });

        result.Budgeted.Remaining.ShouldBe(1200m);
        result.Actual.Remaining.ShouldBe(1260m);
        result.Actual.Income.ShouldBe(3100m);
        result.Categories[Salary].Remaining.ShouldBe(-100m);
    }

    [Fact]
    public void Provisions_reserve_a_share_and_free_the_whole_amount_at_due_date()
    {
        var categories = new List<BudgetCategory> { new(Rent, null, CategoryKind.Fixed, BudgetMode.Amount, 500m) };

        // Période ordinaire : 100 € réservés, la prévision n'en sort rien des comptes.
        var saving = BudgetCalculator.Compute(new BudgetInput(categories, new Dictionary<Guid, decimal>(), new Dictionary<Guid, decimal>(), Thresholds,
            new Dictionary<Guid, ProvisionAmounts> { [Rent] = new(100m, 0m, 0m) }));
        saving.Categories[Rent].Budgeted.ShouldBe(600m);
        saving.Categories[Rent].OwnProvisioned.ShouldBe(100m);
        saving.Categories[Rent].CashBudgeted.ShouldBe(500m);
        saving.Budgeted.Fixed.ShouldBe(600m);

        // Période de l'échéance : la jauge dispose des 1 200 €, le récapitulatif ne compte que la part.
        var due = BudgetCalculator.Compute(new BudgetInput(categories, new Dictionary<Guid, decimal> { [Rent] = -1700m }, new Dictionary<Guid, decimal>(), Thresholds,
            new Dictionary<Guid, ProvisionAmounts> { [Rent] = new(100m, 100m, 1200m) }));
        due.Categories[Rent].Budgeted.ShouldBe(1700m);
        due.Categories[Rent].Status.ShouldBe(BudgetStatus.Exceeded);
        due.Categories[Rent].Remaining.ShouldBe(0m);
        due.Categories[Rent].CashBudgeted.ShouldBe(1700m);
        due.Budgeted.Fixed.ShouldBe(600m);
    }

    [Fact]
    public void Carried_over_remainder_widens_the_gauge_and_the_cash_but_not_the_summary()
    {
        var result = BudgetCalculator.Compute(new BudgetInput(
            [new(Groceries, null, CategoryKind.Variable, BudgetMode.Amount, 400m), new(Fuel, null, CategoryKind.Variable, BudgetMode.Amount, 100m)],
            new Dictionary<Guid, decimal> { [Groceries] = -450m, [Fuel] = -20m },
            new Dictionary<Guid, decimal>(),
            Thresholds,
            CarriedOver: new Dictionary<Guid, decimal> { [Groceries] = 100m, [Fuel] = -50m }));

        var groceries = result.Categories[Groceries];
        groceries.OwnCarriedOver.ShouldBe(100m);
        groceries.Budgeted.ShouldBe(500m);
        groceries.CashBudgeted.ShouldBe(500m);
        groceries.Remaining.ShouldBe(50m);
        groceries.OwnActual.ShouldBe(450m);
        groceries.Status.ShouldBe(BudgetStatus.Warning);

        // Un dépassement reporté réduit le budget de la période suivante.
        result.Categories[Fuel].Budgeted.ShouldBe(50m);
        result.Categories[Fuel].Remaining.ShouldBe(30m);

        // Le récapitulatif ne retient que ce que les revenus de la période financent.
        result.Budgeted.Variable.ShouldBe(500m);
    }
}
