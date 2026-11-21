using HomeBudget.Api.Domain.Backups;

namespace HomeBudget.Tests.Domain;

public sealed class BackupRetentionTests
{
    private static List<DateOnly> Days(DateOnly last, int count) =>
        [.. Enumerable.Range(0, count).Select(i => last.AddDays(-i))];

    [Fact]
    public void Keeps_the_last_daily_backups()
    {
        var last = new DateOnly(2026, 11, 21);

        var keep = BackupRetention.Keep(Days(last, 10), new BackupRetention.Policy(3, 0, 0));

        keep.ShouldBe([last, last.AddDays(-1), last.AddDays(-2)], ignoreOrder: true);
    }

    [Fact]
    public void Keeps_the_latest_backup_of_each_week_and_month()
    {
        // 400 jours de sauvegardes quotidiennes, jusqu'au samedi 21/11/2026.
        var last = new DateOnly(2026, 11, 21);

        var keep = BackupRetention.Keep(Days(last, 400), new BackupRetention.Policy(7, 4, 12));

        // 7 jours (15 → 21/11) ; semaines : dimanches 15/11, 08/11, 01/11 en plus du 21/11 ;
        // mois : derniers jours d'octobre 2026 à décembre 2025 (11 de plus que novembre).
        keep.Count.ShouldBe(7 + 2 + 11);
        keep.ShouldContain(new DateOnly(2026, 11, 8));
        keep.ShouldContain(new DateOnly(2026, 11, 1));
        keep.ShouldContain(new DateOnly(2026, 10, 31));
        keep.ShouldContain(new DateOnly(2025, 12, 31));
        keep.ShouldNotContain(new DateOnly(2025, 11, 30));
        keep.ShouldNotContain(new DateOnly(2026, 11, 14));
    }

    [Fact]
    public void Iso_weeks_span_year_boundaries()
    {
        // Le 01/01/2027 (vendredi) appartient à la semaine ISO 53 de 2026, comme le 31/12/2026.
        var dates = new[] { new DateOnly(2026, 12, 31), new DateOnly(2027, 1, 1) };

        var keep = BackupRetention.Keep(dates, new BackupRetention.Policy(0, 1, 0));

        keep.ShouldBe([new DateOnly(2027, 1, 1)]);
    }

    [Fact]
    public void Zero_everywhere_keeps_nothing()
    {
        BackupRetention.Keep(Days(new DateOnly(2026, 1, 31), 5), new BackupRetention.Policy(0, 0, 0)).ShouldBeEmpty();
    }
}
