namespace HomeBudget.Api.Domain.Family;

/// <summary>État d'une aide attendue (P1).</summary>
public enum BenefitStatus
{
    /// <summary>Reçue : l'opération de l'échéance existe.</summary>
    Received,

    /// <summary>Échue, « à valider » : pas encore confirmée.</summary>
    Pending,

    /// <summary>Sautée : elle n'a pas été versée.</summary>
    Skipped,

    /// <summary>Traitée, mais son opération a été supprimée depuis.</summary>
    Missing,

    /// <summary>À venir dans l'année.</summary>
    Upcoming,

    /// <summary>Reçue hors échéancier : opération d'une catégorie d'aide qui ne vient d'aucune échéance.</summary>
    Unscheduled,
}

/// <summary>Écart d'une aide : reçu − attendu ; null tant que l'aide n'est pas due ou reste à valider.</summary>
public static class BenefitGap
{
    public static decimal? Of(BenefitStatus status, decimal expected, decimal? received) => status switch
    {
        BenefitStatus.Received or BenefitStatus.Unscheduled => (received ?? 0m) - expected,
        BenefitStatus.Skipped or BenefitStatus.Missing => -expected,
        _ => null,
    };

    /// <summary>L'aide entre dans les totaux « échus » : tout, sauf l'à venir.</summary>
    public static bool IsDue(BenefitStatus status) => status != BenefitStatus.Upcoming;
}

/// <summary>Frais de garde d'un mois (P2) : payé, aides à la garde reçues, et reste à charge.</summary>
public readonly record struct ChildcareMonth(int Month, decimal Paid, decimal Aid)
{
    /// <summary>Reste à charge du mois : peut être négatif si l'aide arrive avant la dépense.</summary>
    public decimal Net => Paid - Aid;
}

public static class ChildcareSummary
{
    /// <summary>Montant déclarable sur l'année : frais payés moins aides reçues, jamais négatif.</summary>
    public static decimal Declarable(decimal paid, decimal aid) => Math.Max(0m, paid - aid);
}

/// <summary>Dépense de santé et ses remboursements (P4), tous en positif.</summary>
public readonly record struct HealthBalance(decimal Expense, decimal PrimaryInsurance, decimal Mutual, decimal Other)
{
    public decimal Reimbursed => PrimaryInsurance + Mutual + Other;

    /// <summary>Reste à charge : la dépense moins tous ses remboursements.</summary>
    public decimal Remaining => Expense - Reimbursed;
}
