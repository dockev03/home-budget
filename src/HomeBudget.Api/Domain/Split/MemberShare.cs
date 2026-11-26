namespace HomeBudget.Api.Domain.Split;

/// <summary>Part fixe d'un membre du foyer dans les dépenses communes (K4, mode <see cref="SplitMode.FixedShares"/>).</summary>
public sealed class MemberShare : AuditableEntity
{
    public Guid UserId { get; set; }

    /// <summary>Part en pourcentage (2 décimales) ; les parts du foyer totalisent 100 %.</summary>
    public decimal Percent { get; set; }
}

/// <summary>Mode de répartition des dépenses communes (K4).</summary>
public enum SplitMode
{
    /// <summary>À parts égales entre les membres actifs.</summary>
    Equal,

    /// <summary>Au prorata des revenus de chacun sur la période.</summary>
    IncomeProRata,

    /// <summary>Selon des parts fixes (<see cref="MemberShare"/>).</summary>
    FixedShares,
}
