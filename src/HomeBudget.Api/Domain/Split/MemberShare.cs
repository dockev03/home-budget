namespace HomeBudget.Api.Domain.Split;

/// <summary>
/// Réglages d'un membre du foyer pour la répartition des dépenses communes (K4) : part fixe (mode
/// <see cref="SplitMode.FixedShares"/>) ou exclusion de la répartition.
/// </summary>
public sealed class MemberShare : AuditableEntity
{
    public Guid UserId { get; set; }

    /// <summary>Part en pourcentage (2 décimales) ; les parts du foyer totalisent 100 %.</summary>
    public decimal Percent { get; set; }

    /// <summary>Membre tenu hors de la répartition (compte d'administration, par exemple) : ni part, ni apport, ni solde.</summary>
    public bool ExcludeFromSplit { get; set; }
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
