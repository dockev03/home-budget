namespace HomeBudget.Api.Domain.Family;

/// <summary>
/// Déclaration Pajemploi d'un mois pour un salarié de la garde (P2 bis) : le travail déclaré, la rémunération, les
/// indemnités et le résultat calculé par Pajemploi (cotisations, CMG). Une seule par mois et par salarié, le salarié
/// étant un tiers. Les totaux (coût, CMG, reste à charge) sont recalculés par <see cref="ChildcareDeclarationTotals"/>.
/// </summary>
public sealed class ChildcareDeclaration : AuditableEntity
{
    public int Year { get; set; }

    /// <summary>Mois d'emploi déclaré, de 1 à 12.</summary>
    public int Month { get; set; }

    /// <summary>Salarié (assistant maternel, garde à domicile…), tiers existant.</summary>
    public Guid PayeeId { get; set; }

    // Travail à déclarer.
    public int ActivityDays { get; set; }

    public decimal NormalHours { get; set; }

    public decimal ExtraHours { get; set; }

    public decimal OvertimeHours { get; set; }

    public decimal PaidLeaveDays { get; set; }

    // Rémunération salariale.
    public decimal? NetHourlyRate { get; set; }

    public decimal NetSalary { get; set; }

    // Indemnités et compléments de salaire.
    public decimal MaintenanceAllowance { get; set; }

    public decimal MealAllowance { get; set; }

    public decimal MileageAllowance { get; set; }

    /// <summary>Acompte versé au salarié dans le mois : pour information, il n'entre dans aucun total.</summary>
    public decimal Advance { get; set; }

    // Informations complémentaires.
    public int LongCareDays { get; set; }

    public bool DisabledChildCare { get; set; }

    // Fin de contrat.
    public bool IsContractEnd { get; set; }

    public DateOnly? ContractEndDate { get; set; }

    public decimal PrecariousnessAllowance { get; set; }

    public decimal PaidLeaveCompensation { get; set; }

    public decimal NoticeCompensation { get; set; }

    public decimal TerminationAllowance { get; set; }

    // Résultat de la déclaration.
    public decimal Contributions { get; set; }

    /// <summary>Cotisations prises en charge par la CAF ou la MSA (partie du CMG).</summary>
    public decimal CoveredContributions { get; set; }

    /// <summary>Droit CMG affecté au salaire.</summary>
    public decimal CmgEntitlement { get; set; }
}

/// <summary>Totaux d'une déclaration Pajemploi, calculés comme sur le récapitulatif Pajemploi.</summary>
public readonly record struct ChildcareDeclarationTotals(
    decimal Remuneration, decimal TotalCost, decimal Cmg, decimal Remaining)
{
    public static ChildcareDeclarationTotals Of(ChildcareDeclaration d)
    {
        // Montant du salaire net déclaré : salaire, indemnités et indemnités de fin de contrat (l'acompte en fait partie).
        var remuneration = d.NetSalary + d.MaintenanceAllowance + d.MealAllowance + d.MileageAllowance
            + d.PrecariousnessAllowance + d.PaidLeaveCompensation + d.NoticeCompensation + d.TerminationAllowance;
        var totalCost = remuneration + d.Contributions;
        var cmg = d.CoveredContributions + d.CmgEntitlement;
        return new ChildcareDeclarationTotals(remuneration, totalCost, cmg, totalCost - cmg);
    }
}
