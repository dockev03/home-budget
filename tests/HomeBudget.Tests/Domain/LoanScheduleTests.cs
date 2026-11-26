using HomeBudget.Api.Domain.Accounts;

namespace HomeBudget.Tests.Domain;

/// <summary>Tableau d'amortissement d'un crédit à mensualités constantes (A8).</summary>
public sealed class LoanScheduleTests
{
    [Fact]
    public void Constant_payment_matches_the_usual_formula_and_the_last_installment_clears_the_principal()
    {
        var schedule = LoanSchedule.Compute(200_000m, 3.5m, 240, new DateOnly(2026, 1, 5), 0m);

        schedule.Payment.ShouldBe(1159.92m);
        schedule.Installments.Count.ShouldBe(240);
        var first = schedule.Installments[0];
        first.ShouldBe(new LoanInstallment(1, new DateOnly(2026, 1, 5), 1159.92m, 583.33m, 576.59m, 0m, 199_423.41m));
        var last = schedule.Installments[^1];
        last.Number.ShouldBe(240);
        last.Date.ShouldBe(new DateOnly(2045, 12, 5));
        last.Payment.ShouldBe(1159.78m);
        last.Remaining.ShouldBe(0m);
        schedule.TotalInterest.ShouldBe(78_380.66m);
        schedule.Installments.Sum(i => i.Principal).ShouldBe(200_000m);
        schedule.EndDate.ShouldBe(new DateOnly(2045, 12, 5));
    }

    [Fact]
    public void Insurance_is_added_to_each_installment_and_counted_in_the_total()
    {
        var schedule = LoanSchedule.Compute(12_000m, 3.875m, 48, new DateOnly(2026, 1, 31), 6.5m);

        schedule.Payment.ShouldBe(270.28m);
        schedule.TotalInterest.ShouldBe(973.33m);
        schedule.TotalInsurance.ShouldBe(312m);
        schedule.Installments.ShouldAllBe(i => i.Insurance == 6.5m);
        // Échéances calculées depuis la première (R6) : 31 janvier, 28 février, 31 mars.
        schedule.Installments[1].Date.ShouldBe(new DateOnly(2026, 2, 28));
        schedule.Installments[2].Date.ShouldBe(new DateOnly(2026, 3, 31));
    }

    [Fact]
    public void Zero_rate_spreads_the_principal_and_the_last_installment_takes_the_rounding()
    {
        var schedule = LoanSchedule.Compute(1000m, 0m, 3, new DateOnly(2026, 1, 1), 0m);

        schedule.Installments.Select(i => i.Payment).ShouldBe([333.33m, 333.33m, 333.34m]);
        schedule.TotalInterest.ShouldBe(0m);
    }

    [Fact]
    public void Remaining_principal_counts_the_installments_due_on_or_before_the_date()
    {
        var schedule = LoanSchedule.Compute(12_000m, 3.875m, 48, new DateOnly(2026, 1, 5), 0m);

        schedule.RemainingAt(new DateOnly(2026, 1, 4)).ShouldBe(12_000m);
        schedule.PaidCountAt(new DateOnly(2026, 1, 4)).ShouldBe(0);
        schedule.RemainingAt(new DateOnly(2026, 1, 5)).ShouldBe(11_768.47m);
        schedule.RemainingAt(new DateOnly(2026, 2, 10)).ShouldBe(11_536.19m);
        schedule.PaidCountAt(new DateOnly(2026, 2, 10)).ShouldBe(2);
        schedule.RemainingAt(new DateOnly(2040, 1, 1)).ShouldBe(0m);
    }

    [Theory]
    [InlineData(0, 3, 12)]
    [InlineData(1000, -1, 12)]
    [InlineData(1000, 3, 0)]
    [InlineData(1000, 3, 601)]
    public void Invalid_terms_are_rejected(int principal, int rate, int months) =>
        Should.Throw<ArgumentOutOfRangeException>(() => LoanSchedule.Compute(principal, rate, months, new DateOnly(2026, 1, 1), 0m));
}
