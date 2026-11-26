namespace HomeBudget.Api.Domain.Transactions;

/// <summary>
/// Modification d'une opération (B13) : qui, quand, et l'état avant et après des champs modifiés. Écrite par
/// <c>TransactionHistoryInterceptor</c>, jamais modifiée ensuite ; supprimée avec l'opération.
/// </summary>
public sealed class TransactionChange : Entity
{
    public Guid TransactionId { get; set; }

    public DateTimeOffset At { get; set; }

    public Guid? UserId { get; set; }

    /// <summary>Nom de l'auteur copié à l'enregistrement ; null pour un traitement automatique.</summary>
    public string? UserName { get; set; }

    /// <summary>Champs modifiés, en JSON (liste de <see cref="TransactionFieldChange"/>).</summary>
    public required string Changes { get; set; }
}

/// <summary>Ligne de ventilation telle qu'enregistrée dans l'historique.</summary>
public sealed record TransactionLineSnapshot(Guid? CategoryId, decimal Amount, string? Memo, IReadOnlyList<string> Tags);

/// <summary>
/// Un champ modifié. Les valeurs sont brutes (identifiants, dates ISO, montants) : les noms sont retrouvés à la
/// lecture. La ventilation se compare en entier : <see cref="BeforeLines"/> et <see cref="AfterLines"/>.
/// </summary>
public sealed record TransactionFieldChange(
    string Field,
    string? Before = null,
    string? After = null,
    IReadOnlyList<TransactionLineSnapshot>? BeforeLines = null,
    IReadOnlyList<TransactionLineSnapshot>? AfterLines = null);
