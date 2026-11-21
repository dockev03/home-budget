using System.Globalization;

namespace HomeBudget.Api.Domain.Backups;

/// <summary>
/// Rotation des sauvegardes (O3) façon « grand-père / père / fils » : on garde les N derniers jours,
/// la plus récente de chacune des N dernières semaines (ISO) et de chacun des N derniers mois.
/// </summary>
public static class BackupRetention
{
    public sealed record Policy(int KeepDaily, int KeepWeekly, int KeepMonthly);

    /// <summary>Dates de sauvegarde à conserver parmi <paramref name="backups"/>.</summary>
    public static IReadOnlySet<DateOnly> Keep(IEnumerable<DateOnly> backups, Policy policy)
    {
        var dates = backups.Distinct().OrderByDescending(d => d).ToList();
        var keep = new HashSet<DateOnly>(dates.Take(Math.Max(0, policy.KeepDaily)));

        foreach (var date in LatestPerBucket(dates, IsoWeek).Take(Math.Max(0, policy.KeepWeekly)))
        {
            keep.Add(date);
        }
        foreach (var date in LatestPerBucket(dates, d => (d.Year, d.Month)).Take(Math.Max(0, policy.KeepMonthly)))
        {
            keep.Add(date);
        }
        return keep;
    }

    private static (int, int) IsoWeek(DateOnly date)
    {
        var day = date.ToDateTime(TimeOnly.MinValue);
        return (ISOWeek.GetYear(day), ISOWeek.GetWeekOfYear(day));
    }

    private static IEnumerable<DateOnly> LatestPerBucket(List<DateOnly> datesDescending, Func<DateOnly, (int, int)> bucket) =>
        datesDescending.GroupBy(bucket).Select(g => g.First());
}
