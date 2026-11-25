namespace HomeBudget.Api.Domain.Recurrences;

public enum FrequencyUnit
{
    Day,
    Week,
    Month,
    Year,
}

public enum RecurrenceEnd
{
    /// <summary>Sans fin.</summary>
    Never,

    /// <summary>Dernière échéance au plus tard à <see cref="RecurrenceRule.EndDate"/>.</summary>
    OnDate,

    /// <summary>Après <see cref="RecurrenceRule.Count"/> échéances.</summary>
    AfterCount,
}

/// <summary>
/// Règle de calcul des échéances (E1, E3, R6) : tous les <see cref="Interval"/> jours, semaines, mois ou années
/// depuis <see cref="StartDate"/>. Le trimestriel s'écrit « tous les 3 mois ».
/// </summary>
public sealed record RecurrenceRule(
    DateOnly StartDate,
    FrequencyUnit Unit,
    int Interval,
    RecurrenceEnd End = RecurrenceEnd.Never,
    DateOnly? EndDate = null,
    int? Count = null)
{
    public const int MaxInterval = 999;

    public const int MaxCount = 999;

    /// <summary>
    /// Date de la k-ième échéance (k à partir de 0), calculée depuis la date de départ et non depuis l'échéance
    /// précédente (R6) : un départ le 31 donne le 28 ou 29 février, puis de nouveau le 31 mars.
    /// </summary>
    public DateOnly DateOf(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        var steps = index * Interval;
        return Unit switch
        {
            FrequencyUnit.Day => StartDate.AddDays(steps),
            FrequencyUnit.Week => StartDate.AddDays(7 * steps),
            FrequencyUnit.Month => StartDate.AddMonths(steps),
            FrequencyUnit.Year => StartDate.AddYears(steps),
            _ => throw new InvalidOperationException($"Unité inconnue : {Unit}."),
        };
    }

    /// <summary>Vrai si la k-ième échéance existe au regard de la fin de la règle.</summary>
    public bool Includes(int index, DateOnly date) => End switch
    {
        RecurrenceEnd.AfterCount => index < (Count ?? 0),
        RecurrenceEnd.OnDate => EndDate is { } end && date <= end,
        _ => true,
    };

    /// <summary>Échéances (indice, date) comprises entre <paramref name="from"/> et <paramref name="to"/> inclus.</summary>
    public IEnumerable<ScheduledDate> Between(DateOnly from, DateOnly to)
    {
        if (to < from || to < StartDate)
        {
            yield break;
        }
        for (var index = FirstIndexOnOrAfter(from); ; index++)
        {
            var date = DateOf(index);
            if (date > to || !Includes(index, date))
            {
                yield break;
            }
            if (date >= from)
            {
                yield return new ScheduledDate(index, date);
            }
        }
    }

    /// <summary>Erreurs de la règle, par champ (vide si elle est valide).</summary>
    public Dictionary<string, string[]> Validate()
    {
        var errors = new Dictionary<string, string[]>();
        if (!Enum.IsDefined(Unit))
        {
            errors["unit"] = ["Fréquence inconnue."];
        }
        if (Interval is < 1 or > MaxInterval)
        {
            errors["interval"] = [$"L'intervalle va de 1 à {MaxInterval}."];
        }
        switch (End)
        {
            case RecurrenceEnd.OnDate when EndDate is not { } end || end < StartDate:
                errors["endDate"] = ["La date de fin doit suivre la date de départ."];
                break;
            case RecurrenceEnd.AfterCount when Count is not (>= 1 and <= MaxCount):
                errors["count"] = [$"Le nombre d'échéances va de 1 à {MaxCount}."];
                break;
            case not (RecurrenceEnd.Never or RecurrenceEnd.OnDate or RecurrenceEnd.AfterCount):
                errors["end"] = ["Type de fin inconnu."];
                break;
        }
        return errors;
    }

    /// <summary>Premier indice dont la date peut être ≥ <paramref name="from"/>, sans parcourir tout l'historique.</summary>
    private int FirstIndexOnOrAfter(DateOnly from)
    {
        if (from <= StartDate)
        {
            return 0;
        }
        var days = from.DayNumber - StartDate.DayNumber;
        // Estimation par défaut (un mois compte au plus 31 jours, une année 366), puis avance pas à pas.
        var estimate = Unit switch
        {
            FrequencyUnit.Day => days / Interval,
            FrequencyUnit.Week => days / (7 * Interval),
            FrequencyUnit.Month => days / (31 * Interval),
            FrequencyUnit.Year => days / (366 * Interval),
            _ => 0,
        };
        var index = Math.Max(0, estimate - 1);
        while (DateOf(index) < from)
        {
            index++;
        }
        return index;
    }
}

public readonly record struct ScheduledDate(int Index, DateOnly Date);
