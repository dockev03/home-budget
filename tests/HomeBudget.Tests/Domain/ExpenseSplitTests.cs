using HomeBudget.Api.Domain.Split;

namespace HomeBudget.Tests.Domain;

/// <summary>Répartition des dépenses communes entre les membres (K4).</summary>
public sealed class ExpenseSplitTests
{
    private static readonly Guid A = Guid.Parse("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid B = Guid.Parse("00000000-0000-0000-0000-00000000000b");
    private static readonly Guid C = Guid.Parse("00000000-0000-0000-0000-00000000000c");

    [Fact]
    public void Equal_shares_balance_what_each_member_brought()
    {
        var result = ExpenseSplit.Compute(SplitMode.Equal,
        [
            new SplitMember(A, ExpensesPaid: 600m, Contributions: 400m, Income: 0m, FixedPercent: 0m),
            new SplitMember(B, ExpensesPaid: 200m, Contributions: 0m, Income: 0m, FixedPercent: 0m),
        ]);

        result.AppliedMode.ShouldBe(SplitMode.Equal);
        result.TotalPaid.ShouldBe(1200m);
        result.Shares.Select(s => (s.Share, s.Paid, s.Due, s.Balance)).ShouldBe([(50m, 1000m, 600m, 400m), (50m, 200m, 600m, -400m)]);
        result.Settlements.ShouldHaveSingleItem().ShouldBe(new Settlement(B, A, 400m));
        result.Shares.Sum(s => s.Balance).ShouldBe(0m);
    }

    [Fact]
    public void Income_pro_rata_weights_the_shares_and_falls_back_to_equal_without_income()
    {
        var members = new List<SplitMember>
        {
            new(A, 900m, 0m, Income: 3000m, 0m),
            new(B, 100m, 0m, Income: 1000m, 0m),
        };
        var result = ExpenseSplit.Compute(SplitMode.IncomeProRata, members);
        result.Shares.Select(s => (s.Share, s.Due, s.Balance)).ShouldBe([(75m, 750m, 150m), (25m, 250m, -150m)]);

        var noIncome = ExpenseSplit.Compute(SplitMode.IncomeProRata, [.. members.Select(m => m with { Income = 0m })]);
        noIncome.AppliedMode.ShouldBe(SplitMode.Equal);
        noIncome.Shares.ShouldAllBe(s => s.Due == 500m);
    }

    [Fact]
    public void Fixed_shares_give_the_rounding_rest_to_the_last_member_and_settle_several_debts()
    {
        var result = ExpenseSplit.Compute(SplitMode.FixedShares,
        [
            new SplitMember(A, 100m, 0m, 0m, 33.33m),
            new SplitMember(B, 0m, 0m, 0m, 33.33m),
            new SplitMember(C, 0.01m, 0m, 0m, 33.34m),
        ]);

        result.TotalPaid.ShouldBe(100.01m);
        result.Shares.Sum(s => s.Due).ShouldBe(100.01m);
        result.Shares.Sum(s => s.Balance).ShouldBe(0m);
        result.Settlements.Count.ShouldBe(2);
        result.Settlements.ShouldAllBe(s => s.ToUserId == A);
        result.Settlements.Sum(s => s.Amount).ShouldBe(result.Shares[0].Balance);
    }

    [Fact]
    public void No_member_gives_an_empty_result() =>
        ExpenseSplit.Compute(SplitMode.Equal, []).Shares.ShouldBeEmpty();
}
