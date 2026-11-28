namespace HomeBudget.Api.Domain.Accounts;

/// <summary>
/// Taux de rémunération d'un compte d'épargne (A9), en vigueur à partir de <see cref="EffectiveFrom"/> et jusqu'au
/// taux suivant. Les taux passés forment l'historique, les taux futurs sont des changements programmés. Les intérêts
/// en découlent (<see cref="SavingsInterest"/>). Sa visibilité suit celle du compte (R8).
/// </summary>
public sealed class SavingsRate : AuditableEntity
{
    /// <summary>Décimales du taux annuel, en pourcentage (comme <see cref="Loan.RateDecimals"/>).</summary>
    public const int RateDecimals = Loan.RateDecimals;

    public Guid AccountId { get; set; }

    /// <summary>Premier jour du taux. Fixé en cours de quinzaine, il s'applique à partir de la quinzaine suivante.</summary>
    public DateOnly EffectiveFrom { get; set; }

    /// <summary>Taux annuel, en pourcentage (1,7 pour 1,7 %).</summary>
    public decimal AnnualRate { get; set; }
}
