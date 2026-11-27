using HomeBudget.Api.Domain.Categories;
using HomeBudget.Api.Domain.Family;

namespace HomeBudget.Tests.Domain;

public sealed class FamilyCalculationsTests
{
    [Theory]
    [InlineData(BenefitStatus.Received, 120.0, 115.0, -5.0)]
    [InlineData(BenefitStatus.Received, 120.0, 130.0, 10.0)]
    [InlineData(BenefitStatus.Unscheduled, 0.0, 50.0, 50.0)]
    [InlineData(BenefitStatus.Skipped, 120.0, null, -120.0)]
    [InlineData(BenefitStatus.Missing, 120.0, null, -120.0)]
    public void Gap_is_received_minus_expected(BenefitStatus status, double expected, double? received, double gap) =>
        BenefitGap.Of(status, (decimal)expected, (decimal?)received).ShouldBe((decimal)gap);

    [Theory]
    [InlineData(BenefitStatus.Pending)]
    [InlineData(BenefitStatus.Upcoming)]
    public void No_gap_until_the_benefit_is_settled(BenefitStatus status) =>
        BenefitGap.Of(status, 120m, null).ShouldBeNull();

    [Fact]
    public void Upcoming_benefits_are_not_due() =>
        Enum.GetValues<BenefitStatus>().Where(s => !BenefitGap.IsDue(s)).ShouldBe([BenefitStatus.Upcoming]);

    [Fact]
    public void Declarable_childcare_is_net_of_aid_and_never_negative()
    {
        ChildcareSummary.Declarable(1080m, 300m).ShouldBe(780m);
        ChildcareSummary.Declarable(100m, 300m).ShouldBe(0m);
        new ChildcareMonth(2, 480m, 300m).Net.ShouldBe(180m);
        new ChildcareMonth(3, 0m, 300m).Net.ShouldBe(-300m);
    }

    [Fact]
    public void Health_remaining_is_expense_minus_every_reimbursement()
    {
        var balance = new HealthBalance(50m, 35m, 10m, 2m);
        balance.Reimbursed.ShouldBe(47m);
        balance.Remaining.ShouldBe(3m);
        new HealthBalance(20m, 0m, 0m, 0m).Remaining.ShouldBe(20m);
    }

    [Theory]
    [InlineData(FamilyRole.Benefit, CategoryKind.Income, true)]
    [InlineData(FamilyRole.ChildcareAid, CategoryKind.Income, true)]
    [InlineData(FamilyRole.Benefit, CategoryKind.Fixed, false)]
    [InlineData(FamilyRole.Childcare, CategoryKind.Fixed, true)]
    [InlineData(FamilyRole.Health, CategoryKind.Variable, true)]
    [InlineData(FamilyRole.Health, CategoryKind.Income, false)]
    [InlineData(FamilyRole.Childcare, CategoryKind.Savings, false)]
    [InlineData(FamilyRole.None, CategoryKind.Savings, true)]
    public void Role_fits_the_category_kind(FamilyRole role, CategoryKind kind, bool fits) =>
        Category.Fits(role, kind).ShouldBe(fits);

    [Fact]
    public void A_subcategory_without_role_takes_its_parent_role()
    {
        var parent = new Category { Name = "Santé", Kind = CategoryKind.Variable, FamilyRole = FamilyRole.Health };
        var child = new Category { Name = "Dentiste", Kind = CategoryKind.Variable, ParentId = parent.Id };
        var own = new Category { Name = "Pharmacie", Kind = CategoryKind.Variable, ParentId = parent.Id, FamilyRole = FamilyRole.Childcare };
        var all = new[] { parent, child, own }.ToDictionary(c => c.Id);
        child.EffectiveRole(all).ShouldBe(FamilyRole.Health);
        own.EffectiveRole(all).ShouldBe(FamilyRole.Childcare);
        parent.EffectiveRole(all).ShouldBe(FamilyRole.Health);
    }

    [Fact]
    public void Default_categories_carry_their_role()
    {
        DefaultCategories.RoleOf("CMG", CategoryKind.Income).ShouldBe(FamilyRole.ChildcareAid);
        DefaultCategories.RoleOf("Santé", CategoryKind.Variable).ShouldBe(FamilyRole.Health);
        DefaultCategories.RoleOf("Santé", CategoryKind.Income).ShouldBe(FamilyRole.None);
        DefaultCategories.RoleOf("Inconnue", CategoryKind.Income).ShouldBe(FamilyRole.None);
    }
}
