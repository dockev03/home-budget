namespace HomeBudget.Api.Domain.Recurrences;

/// <summary>Report d'une échéance qui tombe un week-end ou un jour férié (E9).</summary>
public enum BusinessDayShift
{
    /// <summary>Pas de report.</summary>
    None,

    /// <summary>Report au jour ouvré suivant.</summary>
    Next,

    /// <summary>Avance au jour ouvré précédent.</summary>
    Previous,
}

/// <summary>
/// Jours ouvrés et jours fériés de France métropolitaine (E9). Les jours fériés propres à l'Alsace-Moselle
/// (Vendredi saint, 26 décembre) ne sont pas comptés.
/// </summary>
public static class BusinessDays
{
    /// <summary>Plus grand écart possible entre une date et son jour ouvré (week-end prolongé par un férié).</summary>
    public const int MaxShiftDays = 4;

    public static bool IsBusinessDay(DateOnly date) =>
        date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday) && !IsHoliday(date);

    /// <summary>Jour férié légal : dates fixes, puis lundi de Pâques, Ascension et lundi de Pentecôte.</summary>
    public static bool IsHoliday(DateOnly date)
    {
        if ((date.Month, date.Day) is (1, 1) or (5, 1) or (5, 8) or (7, 14) or (8, 15) or (11, 1) or (11, 11) or (12, 25))
        {
            return true;
        }
        var easter = Easter(date.Year);
        return date == easter.AddDays(1) || date == easter.AddDays(39) || date == easter.AddDays(50);
    }

    /// <summary>Date appliquée selon le report choisi ; inchangée si c'est déjà un jour ouvré.</summary>
    public static DateOnly Adjust(DateOnly date, BusinessDayShift shift)
    {
        if (shift == BusinessDayShift.None)
        {
            return date;
        }
        var step = shift == BusinessDayShift.Next ? 1 : -1;
        while (!IsBusinessDay(date))
        {
            date = date.AddDays(step);
        }
        return date;
    }

    /// <summary>Dimanche de Pâques (calendrier grégorien, algorithme de Meeus/Jones/Butcher).</summary>
    public static DateOnly Easter(int year)
    {
        var a = year % 19;
        var b = year / 100;
        var c = year % 100;
        var d = b / 4;
        var e = b % 4;
        var f = (b + 8) / 25;
        var g = (b - f + 1) / 3;
        var h = ((19 * a) + b - d - g + 15) % 30;
        var i = c / 4;
        var k = c % 4;
        var l = (32 + (2 * e) + (2 * i) - h - k) % 7;
        var m = (a + (11 * h) + (22 * l)) / 451;
        var month = (h + l - (7 * m) + 114) / 31;
        var day = ((h + l - (7 * m) + 114) % 31) + 1;
        return new DateOnly(year, month, day);
    }
}
