using System.Globalization;

using HomeBudget.Api.Domain.Settings;

namespace HomeBudget.Api.Domain.Periods;

/// <summary>
/// Période budgétaire (R1, DM4) : intervalle de dates inclusif, identifié par la clé <c>AAAA-MM</c>
/// du mois dont elle porte le nom.
/// </summary>
public readonly record struct Period(DateOnly Start, DateOnly End, int Year, int Month)
{
    public string Key => $"{Year:D4}-{Month:D2}";

    public int Days => End.DayNumber - Start.DayNumber + 1;

    public bool Contains(DateOnly date) => date >= Start && date <= End;
}

/// <summary>
/// Calcul des périodes selon le paramètre du foyer (R1). Tous les calculs passent par ici,
/// jamais par le mois calendaire en dur.
/// </summary>
/// <remarks>
/// En mode jour fixe N, la période va du N du mois jusqu'à la veille du N suivant. Elle porte le nom
/// du mois qui contient la majorité de ses jours : le mois de début si N ≤ 15, le mois de fin sinon
/// (du 25 octobre au 24 novembre → novembre). La règle dépend de N seul, et pas de la longueur des
/// mois, pour que chaque mois nomme exactement une période (sans trou ni doublon autour de février).
/// </remarks>
public sealed class PeriodCalculator
{
    public const int MaxStartDay = 28;

    private const int LastDayNamedByStartMonth = 15;

    private readonly int _startDay;

    public PeriodCalculator(PeriodMode mode, int startDay)
    {
        if (mode == PeriodMode.FixedDay && startDay is < 1 or > MaxStartDay)
        {
            throw new ArgumentOutOfRangeException(nameof(startDay), startDay, $"Le jour de début va de 1 à {MaxStartDay}.");
        }
        _startDay = mode == PeriodMode.Calendar ? 1 : startDay;
    }

    public static PeriodCalculator From(HouseholdSettings settings) => new(settings.PeriodMode, settings.PeriodStartDay);

    /// <summary>Période qui contient la date.</summary>
    public Period For(DateOnly date)
    {
        var month = new DateOnly(date.Year, date.Month, 1);
        if (date.Day < _startDay)
        {
            month = month.AddMonths(-1);
        }
        return FromStart(new DateOnly(month.Year, month.Month, _startDay));
    }

    public Period Next(Period period) => For(period.End.AddDays(1));

    public Period Previous(Period period) => For(period.Start.AddDays(-1));

    /// <summary>Période désignée par sa clé <c>AAAA-MM</c> ; false si la clé est invalide.</summary>
    public bool TryParse(string? key, out Period period)
    {
        period = default;
        if (key is not { Length: 7 } || key[4] != '-'
            || !int.TryParse(key.AsSpan(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out var year)
            || !int.TryParse(key.AsSpan(5, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var month)
            || year is < 1900 or > 9998 || month is < 1 or > 12)
        {
            return false;
        }
        var start = new DateOnly(year, month, _startDay);
        if (!NamedByStartMonth)
        {
            start = start.AddMonths(-1);
        }
        period = FromStart(start);
        return true;
    }

    private bool NamedByStartMonth => _startDay <= LastDayNamedByStartMonth;

    private Period FromStart(DateOnly start)
    {
        var end = start.AddMonths(1).AddDays(-1);
        var named = NamedByStartMonth ? start : end;
        return new Period(start, end, named.Year, named.Month);
    }
}
