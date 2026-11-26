namespace HomeBudget.Api.Domain.Recurrences;

public enum RecurrenceMode
{
    /// <summary>L'opération est créée, non pointée, le jour de l'échéance (E4).</summary>
    Automatic,

    /// <summary>L'échéance attend d'être confirmée, sautée ou reportée (E5).</summary>
    ToValidate,
}

/// <summary>
/// Opération ou virement récurrent (section E). Un virement récurrent a un <see cref="ToAccountId"/> :
/// son montant est alors positif, et seule une catégorie d'épargne est admise (R5).
/// </summary>
public sealed class Recurrence : AuditableEntity
{
    public required string Label { get; set; }

    public Guid AccountId { get; set; }

    /// <summary>Compte de destination d'un virement récurrent.</summary>
    public Guid? ToAccountId { get; set; }

    public Guid? PayeeId { get; set; }

    public Guid? CategoryId { get; set; }

    /// <summary>Montant signé (négatif pour une dépense) ; positif pour un virement.</summary>
    public decimal Amount { get; set; }

    public string? Notes { get; set; }

    public RecurrenceMode Mode { get; set; }

    public DateOnly StartDate { get; set; }

    public FrequencyUnit Unit { get; set; } = FrequencyUnit.Month;

    public int Interval { get; set; } = 1;

    public RecurrenceEnd End { get; set; }

    public DateOnly? EndDate { get; set; }

    public int? Count { get; set; }

    /// <summary>Paiement en N fois (E2) : libellé numéroté, et fin après <see cref="Count"/> échéances.</summary>
    public bool IsInstallment { get; set; }

    /// <summary>Écart de montant admis pour rapprocher une opération importée (E7), en euros ou en pourcentage.</summary>
    public decimal Tolerance { get; set; }

    public ToleranceUnit ToleranceUnit { get; set; }

    /// <summary>Abonnement (E10) : compté dans la vue « Abonnements ».</summary>
    public bool IsSubscription { get; set; }

    public AmountTolerance AmountTolerance => new(Tolerance, ToleranceUnit);

    /// <summary>Date à partir de laquelle les échéances produisent des opérations (création ou changement de règle).</summary>
    public DateOnly GenerateFrom { get; set; }

    public List<RecurrenceOccurrence> Occurrences { get; private set; } = [];

    public RecurrenceRule Rule => new(StartDate, Unit, Interval, End, EndDate, Count);

    public IReadOnlyCollection<OccurrenceState> States =>
        [.. Occurrences.Select(o => new OccurrenceState(o.ScheduledDate, o.Status, o.DueDate))];
}

/// <summary>
/// Échéance traitée d'une récurrence. L'unicité (récurrence, date prévue) garantit qu'une échéance n'est
/// jamais générée deux fois, même si l'opération créée est ensuite supprimée (R6).
/// </summary>
public sealed class RecurrenceOccurrence : AuditableEntity
{
    public Guid RecurrenceId { get; set; }

    /// <summary>Rang de l'échéance (0 pour la première).</summary>
    public int Index { get; set; }

    public DateOnly ScheduledDate { get; set; }

    /// <summary>Date d'effet : la date prévue, ou celle du report.</summary>
    public DateOnly DueDate { get; set; }

    public OccurrenceStatus Status { get; set; }

    /// <summary>Opération créée (la sortie, pour un virement) ; null si elle a été supprimée depuis.</summary>
    public Guid? TransactionId { get; set; }
}
