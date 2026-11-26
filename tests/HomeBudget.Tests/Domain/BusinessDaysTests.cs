using HomeBudget.Api.Domain.Recurrences;

namespace HomeBudget.Tests.Domain;

public sealed class BusinessDaysTests
{
    private static DateOnly D(int year, int month, int day) => new(year, month, day);

    [Theory]
    [InlineData(2024, 3, 31)]
    [InlineData(2025, 4, 20)]
    [InlineData(2026, 4, 5)]
    [InlineData(2027, 3, 28)]
    [InlineData(2038, 4, 25)]
    public void Easter_sunday(int year, int month, int day) => BusinessDays.Easter(year).ShouldBe(D(year, month, day));

    [Fact]
    public void French_public_holidays_of_2026()
    {
        DateOnly[] holidays =
        [
            D(2026, 1, 1), D(2026, 4, 6), D(2026, 5, 1), D(2026, 5, 8), D(2026, 5, 14), D(2026, 5, 25),
            D(2026, 7, 14), D(2026, 8, 15), D(2026, 11, 1), D(2026, 11, 11), D(2026, 12, 25),
        ];
        var found = Enumerable.Range(0, 365).Select(i => D(2026, 1, 1).AddDays(i)).Where(BusinessDays.IsHoliday).ToList();
        found.ShouldBe(holidays);
    }

    [Fact]
    public void Adjust_moves_to_next_or_previous_business_day()
    {
        // Samedi 2 mai 2026 → lundi 4 mai ; vendredi 1er mai férié → jeudi 30 avril.
        BusinessDays.Adjust(D(2026, 5, 2), BusinessDayShift.Next).ShouldBe(D(2026, 5, 4));
        BusinessDays.Adjust(D(2026, 5, 2), BusinessDayShift.Previous).ShouldBe(D(2026, 4, 30));
        // Samedi 4 avril 2026, lundi de Pâques le 6 → mardi 7.
        BusinessDays.Adjust(D(2026, 4, 4), BusinessDayShift.Next).ShouldBe(D(2026, 4, 7));
        BusinessDays.Adjust(D(2026, 4, 8), BusinessDayShift.Next).ShouldBe(D(2026, 4, 8));
        BusinessDays.Adjust(D(2026, 5, 2), BusinessDayShift.None).ShouldBe(D(2026, 5, 2));
    }
}
