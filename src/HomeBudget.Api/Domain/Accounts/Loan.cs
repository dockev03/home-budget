namespace HomeBudget.Api.Domain.Accounts;

/// <summary>
/// Conditions d'un crédit (A8), rattachées à un compte de type crédit : capital emprunté, taux annuel, durée et
/// assurance. Le tableau d'amortissement et le capital restant dû en découlent (<see cref="LoanSchedule"/>). Sa
/// visibilité suit celle du compte (R8).
/// </summary>
public sealed class Loan : AuditableEntity
{
    /// <summary>Nombre maximal de mensualités (50 ans).</summary>
    public const int MaxDurationMonths = 600;

    /// <summary>Décimales du taux annuel, en pourcentage (3,8750 %).</summary>
    public const int RateDecimals = 4;

    public Guid AccountId { get; set; }

    /// <summary>Capital emprunté, en positif.</summary>
    public decimal Principal { get; set; }

    /// <summary>Taux nominal annuel, en pourcentage (3,85 pour 3,85 %).</summary>
    public decimal AnnualRate { get; set; }

    public int DurationMonths { get; set; }

    /// <summary>Date de la première mensualité ; les suivantes tombent le même jour des mois suivants.</summary>
    public DateOnly FirstPaymentDate { get; set; }

    /// <summary>Assurance emprunteur, montant fixe ajouté à chaque mensualité.</summary>
    public decimal MonthlyInsurance { get; set; }
}
