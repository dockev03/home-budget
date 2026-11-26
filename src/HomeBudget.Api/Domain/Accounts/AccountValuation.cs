namespace HomeBudget.Api.Domain.Accounts;

/// <summary>
/// Valorisation manuelle d'un compte d'investissement (A7) : sa valeur à la fin du jour <see cref="Date"/>. Le solde
/// du compte part de la dernière valorisation et y ajoute les opérations datées après elle. Sa visibilité suit celle
/// du compte (R8).
/// </summary>
public sealed class AccountValuation : AuditableEntity
{
    public const int MaxNoteLength = 200;

    public Guid AccountId { get; set; }

    public DateOnly Date { get; set; }

    public decimal Value { get; set; }

    public string? Note { get; set; }
}
