namespace HomeBudget.Api.Domain.Reconciliations;

/// <summary>
/// Rapprochement bancaire (A5) : le solde d'un relevé à une date, égal au solde des opérations pointées du compte
/// à cette date. Les opérations rapprochées sont verrouillées (R7) tant que le rapprochement n'est pas annulé.
/// </summary>
public sealed class Reconciliation : AuditableEntity
{
    public Guid AccountId { get; set; }

    /// <summary>Date du relevé : les opérations pointées jusqu'à cette date sont rapprochées.</summary>
    public DateOnly Date { get; set; }

    /// <summary>Solde indiqué par le relevé de la banque à cette date.</summary>
    public decimal StatementBalance { get; set; }
}
