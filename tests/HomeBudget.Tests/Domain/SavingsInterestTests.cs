using HomeBudget.Api.Domain.Accounts;

namespace HomeBudget.Tests.Domain;

/// <summary>Intérêts d'un livret par quinzaine (A9).</summary>
public sealed class SavingsInterestTests
{
    // À 2,4 %, 1 000 € rapportent exactement 1 € par quinzaine : les résultats se comptent en quinzaines.
    private static readonly SavingsRatePeriod[] FlatRate = [new(new DateOnly(2000, 1, 1), 2.4m)];

    private static SavingsMovement Move(int year, int month, int day, decimal amount) => new(new DateOnly(year, month, day), amount);

    [Fact]
    public void Year_has_24_fortnights_from_the_1st_to_the_15th_and_from_the_16th_to_the_end_of_the_month()
    {
        var fortnights = SavingsInterest.FortnightsOf(2028).ToList();

        fortnights.Count.ShouldBe(24);
        fortnights[0].ShouldBe((new DateOnly(2028, 1, 1), new DateOnly(2028, 1, 15)));
        fortnights[3].ShouldBe((new DateOnly(2028, 2, 16), new DateOnly(2028, 2, 29)));
        fortnights[^1].ShouldBe((new DateOnly(2028, 12, 16), new DateOnly(2028, 12, 31)));
    }

    [Fact]
    public void Each_fortnight_earns_a_24th_of_the_annual_rate_whatever_its_length()
    {
        // 2 500 € à 1,7 % pendant 20 quinzaines (versés le 28 février, ils rapportent dès le 1er mars).
        var interest = SavingsInterest.Compute(2026, [Move(2026, 2, 28, 2500m)], [new(new DateOnly(2026, 1, 1), 1.7m)]);

        interest.Total.ShouldBe(35.42m);
        interest.Fortnights.Count(f => f.Interest > 0).ShouldBe(20);
        interest.Fortnights[3].Interest.ShouldBe(0m);
        interest.Fortnights[4].Base.ShouldBe(2500m);
    }

    [Theory]
    [InlineData(1, 23)]
    [InlineData(15, 23)]
    [InlineData(16, 22)]
    [InlineData(31, 22)]
    public void A_deposit_earns_from_the_next_fortnight(int day, int fortnights)
    {
        var interest = SavingsInterest.Compute(2026, [Move(2026, 1, day, 1000m)], FlatRate);

        interest.Total.ShouldBe(fortnights);
    }

    [Theory]
    [InlineData(6, 30, 11)]
    [InlineData(7, 1, 12)]
    [InlineData(7, 15, 12)]
    [InlineData(7, 16, 13)]
    public void A_withdrawal_stops_earning_from_the_start_of_its_fortnight(int month, int day, int fortnights)
    {
        var interest = SavingsInterest.Compute(2026, [Move(2025, 12, 31, 1000m), Move(2026, month, day, -1000m)], FlatRate);

        interest.Total.ShouldBe(fortnights);
    }

    [Fact]
    public void Deposit_and_withdrawal_in_the_same_fortnight_earn_nothing_and_the_base_never_goes_negative()
    {
        var interest = SavingsInterest.Compute(
            2026, [Move(2025, 12, 31, 500m), Move(2026, 3, 3, 1000m), Move(2026, 3, 10, -1000m)], FlatRate);

        // Le retrait de 1 000 € vaut dès le 1er mars : la quinzaine ne rapporte rien, puis 500 € rapportent à nouveau.
        interest.Fortnights[4].Base.ShouldBe(0m);
        interest.Fortnights[5].Base.ShouldBe(500m);
        interest.Total.ShouldBe(23 * 0.5m);
    }

    [Fact]
    public void Rate_changes_apply_from_their_date_and_a_change_within_a_fortnight_from_the_next_one()
    {
        // Livret A : 1,5 % puis 1,7 % au 1er août 2026.
        SavingsRatePeriod[] livretA = [new(new DateOnly(2025, 8, 1), 1.5m), new(new DateOnly(2026, 8, 1), 1.7m)];
        var regulated = SavingsInterest.Compute(2026, [Move(2025, 6, 1, 1200m)], livretA);
        regulated.Total.ShouldBe(10.50m + 8.50m);
        regulated.Fortnights[13].AnnualRate.ShouldBe(1.5m);
        regulated.Fortnights[14].AnnualRate.ShouldBe(1.7m);

        // Taux doublé le 10 janvier : la 1re quinzaine garde l'ancien taux.
        SavingsRatePeriod[] boosted = [.. FlatRate, new(new DateOnly(2026, 1, 10), 4.8m)];
        SavingsInterest.Compute(2026, [Move(2025, 12, 31, 1000m)], boosted).Total.ShouldBe(1m + (23 * 2m));
    }

    [Fact]
    public void No_rate_means_no_interest_and_later_movements_are_ignored()
    {
        SavingsInterest.Compute(2026, [Move(2025, 1, 1, 1000m)], []).Total.ShouldBe(0m);
        SavingsInterest.Compute(2026, [Move(2025, 1, 1, 1000m)], [new(new DateOnly(2026, 7, 1), 2.4m)]).Total.ShouldBe(12m);
        SavingsInterest.Compute(2026, [Move(2025, 1, 1, 1000m), Move(2027, 1, 1, -1000m)], FlatRate).Total.ShouldBe(24m);
    }

    [Fact]
    public void Earned_interest_counts_only_the_fortnights_ended_at_the_date()
    {
        var interest = SavingsInterest.Compute(2026, [Move(2025, 12, 31, 1000m)], FlatRate);

        interest.EarnedAt(new DateOnly(2025, 12, 31)).ShouldBe(0m);
        interest.EarnedAt(new DateOnly(2026, 3, 14)).ShouldBe(4m);
        interest.EarnedAt(new DateOnly(2026, 3, 15)).ShouldBe(5m);
        interest.EarnedAt(new DateOnly(2026, 12, 31)).ShouldBe(interest.Total);
    }
}
