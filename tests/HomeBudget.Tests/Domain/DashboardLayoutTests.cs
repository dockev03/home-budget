using HomeBudget.Api.Domain.Dashboard;

namespace HomeBudget.Tests.Domain;

public sealed class DashboardLayoutTests
{
    [Fact]
    public void Without_a_choice_the_default_order_applies_with_nothing_hidden()
    {
        var (order, hidden) = DashboardLayout.Normalize(null, null);

        order.ShouldBe(DashboardLayout.DefaultOrder);
        hidden.ShouldBeEmpty();
    }

    [Fact]
    public void Unknown_and_duplicate_blocks_are_ignored_and_missing_ones_go_last()
    {
        var (order, hidden) = DashboardLayout.Normalize(
            ["accounts", "removedBlock", "keyFigures", "accounts", "history"],
            ["history", "removedBlock", "accounts", "history"]);

        order.ShouldBe(["accounts", "keyFigures", "history", "forecast", "charts", "comparison", "topPayees", "wealth"]);
        hidden.ShouldBe(["accounts", "history"]);
    }
}
