using HomeBudget.Api.Domain.Accounts;

namespace HomeBudget.Tests.Domain;

public sealed class BalanceHistoryTests
{
    private static readonly DateOnly Opening = new(2026, 1, 10);

    private static DatedAmount On(int month, int day, decimal amount) => new(new DateOnly(2026, month, day), amount);

    [Fact]
    public void Without_valuation_the_balance_is_the_initial_balance_plus_the_movements_up_to_the_date()
    {
        var history = new BalanceHistory(1000m, Opening, [], [On(1, 15, -100m), On(2, 1, 50m), On(3, 1, -20m)]);

        history.At(new DateOnly(2026, 1, 31)).ShouldBe(900m);
        history.At(new DateOnly(2026, 2, 1)).ShouldBe(950m);
        history.At(new DateOnly(2026, 12, 31)).ShouldBe(930m);
    }

    [Fact]
    public void Before_its_opening_an_account_is_worth_nothing()
    {
        var history = new BalanceHistory(1000m, Opening, [], []);

        history.At(Opening.AddDays(-1)).ShouldBe(0m);
        history.At(Opening).ShouldBe(1000m);
    }

    [Fact]
    public void A_valuation_replaces_everything_before_it_and_the_later_movements_are_added()
    {
        // Valorisation du 31/01 : 5 200 €, opérations du jour comprises. Versement de 300 € le 15/02.
        var history = new BalanceHistory(
            5000m, Opening, [On(3, 31, 5600m), On(1, 31, 5200m)], [On(1, 31, 100m), On(2, 15, 300m), On(4, 2, 200m)]);

        history.At(new DateOnly(2026, 1, 30)).ShouldBe(5000m);
        history.At(new DateOnly(2026, 1, 31)).ShouldBe(5200m);
        history.At(new DateOnly(2026, 2, 28)).ShouldBe(5500m);
        // La dernière valorisation antérieure à la date l'emporte, quel que soit l'ordre de la liste.
        history.At(new DateOnly(2026, 3, 31)).ShouldBe(5600m);
        history.At(new DateOnly(2026, 4, 30)).ShouldBe(5800m);
    }
}
