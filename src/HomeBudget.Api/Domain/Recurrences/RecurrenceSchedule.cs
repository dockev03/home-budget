namespace HomeBudget.Api.Domain.Recurrences;

public enum OccurrenceStatus
{
    /// <summary>Échéance « à valider » (E5), en attente de confirmation.</summary>
    Pending,

    /// <summary>Opération créée (automatiquement ou après confirmation).</summary>
    Generated,

    /// <summary>Échéance sautée : elle n'aura pas lieu.</summary>
    Skipped,

    /// <summary>Échéance remplacée par une opération importée du relevé (E6).</summary>
    Matched,
}

/// <summary>État d'une échéance déjà traitée : sa date prévue, et sa date d'effet (reportée ou non).</summary>
public readonly record struct OccurrenceState(DateOnly ScheduledDate, OccurrenceStatus Status, DateOnly DueDate);

/// <summary>Échéance attendue : <see cref="Date"/> est la date d'effet (une échéance reportée garde sa date prévue).</summary>
public readonly record struct ExpectedOccurrence(int Index, DateOnly ScheduledDate, DateOnly Date);

/// <summary>
/// Traitement des échéances d'une récurrence, sans accès à la base. Une échéance générée, sautée ou supprimée
/// n'est jamais régénérée (R6) : toute date déjà présente dans <paramref name="occurrences"/> est ignorée.
/// </summary>
/// <remarks>
/// <c>generateFrom</c> est la date à partir de laquelle la récurrence produit des opérations (sa création, ou sa
/// dernière modification de règle) : une récurrence commencée dans le passé ne recrée pas l'historique, mais
/// garde la numérotation de ses échéances (« Montre 3/4 »).
/// </remarks>
public static class RecurrenceSchedule
{
    /// <summary>Échéances à traiter au jour <paramref name="today"/> : prévues depuis <paramref name="generateFrom"/>, pas encore traitées.</summary>
    public static IReadOnlyList<ScheduledDate> DueOn(
        RecurrenceRule rule, DateOnly generateFrom, IReadOnlyCollection<OccurrenceState> occurrences, DateOnly today)
    {
        var known = occurrences.Select(o => o.ScheduledDate).ToHashSet();
        return [.. rule.Between(generateFrom, today).Where(s => !known.Contains(s.Date))];
    }

    /// <summary>
    /// Échéances restantes jusqu'à <paramref name="until"/> (R3) : celles « à valider » en attente, et celles
    /// prévues qui n'ont pas encore été traitées.
    /// </summary>
    public static IReadOnlyList<ExpectedOccurrence> Remaining(
        RecurrenceRule rule, DateOnly generateFrom, IReadOnlyCollection<OccurrenceState> occurrences, DateOnly until)
    {
        var known = occurrences.Select(o => o.ScheduledDate).ToHashSet();
        var pending = occurrences
            .Where(o => o.Status == OccurrenceStatus.Pending && o.DueDate <= until)
            .Select(o => new ExpectedOccurrence(IndexOf(rule, o.ScheduledDate), o.ScheduledDate, o.DueDate));
        var upcoming = rule.Between(generateFrom, until)
            .Where(s => !known.Contains(s.Date))
            .Select(s => new ExpectedOccurrence(s.Index, s.Date, s.Date));
        return [.. pending.Concat(upcoming).OrderBy(o => o.Date)];
    }

    /// <summary>
    /// Échéances prévues sur l'intervalle (budget Auto, D2), sautées exceptées, à leur date prévue.
    /// </summary>
    public static IReadOnlyList<ScheduledDate> Planned(
        RecurrenceRule rule, IReadOnlyCollection<OccurrenceState> occurrences, DateOnly from, DateOnly to)
    {
        var skipped = occurrences.Where(o => o.Status == OccurrenceStatus.Skipped).Select(o => o.ScheduledDate).ToHashSet();
        return [.. rule.Between(from, to).Where(s => !skipped.Contains(s.Date))];
    }

    /// <summary>Libellé d'une échéance : numéroté pour un paiement en N fois (E2), « Montre 1/4 ».</summary>
    public static string Label(string label, bool isInstallment, int index, int? count) =>
        isInstallment && count is { } total ? $"{label} {index + 1}/{total}" : label;

    private static int IndexOf(RecurrenceRule rule, DateOnly scheduledDate) =>
        rule.Between(scheduledDate, scheduledDate).Select(s => s.Index).DefaultIfEmpty(-1).First();
}
