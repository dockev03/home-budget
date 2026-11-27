using System.ComponentModel.DataAnnotations;

namespace HomeBudget.Api.Features.Transactions;

public sealed record TransactionLineRequest(
    Guid? CategoryId,
    [property: Range(-999_999_999, 999_999_999)] decimal Amount,
    [property: StringLength(200)] string? Memo,
    IReadOnlyList<string>? Tags = null);

public sealed record TransactionRequest(
    Guid AccountId,
    DateOnly Date,
    [property: StringLength(100)] string? PayeeName,
    [property: StringLength(200)] string? Label,
    [property: StringLength(2000)] string? Notes,
    bool? IsCleared,
    [property: Required, MinLength(1), MaxLength(50)] IReadOnlyList<TransactionLineRequest> Lines);

public sealed record TransactionLineDto(Guid Id, Guid? CategoryId, decimal Amount, string? Memo, IReadOnlyList<string> Tags);

/// <param name="IsReconciled">Opération rapprochée (A5) : verrouillée.</param>
/// <param name="ReimbursesId">Dépense remboursée par ce revenu (B11).</param>
/// <param name="ReimbursedAmount">Pour une dépense, total des remboursements liés : coût net = montant + remboursé.</param>
/// <param name="AdvanceId">Avance soldée en tout ou partie par ce revenu (B12).</param>
/// <param name="CreatedBy">Auteur de l'opération (K2) ; null pour une opération générée ou importée sans session.</param>
/// <param name="AttachmentCount">Nombre de pièces jointes (B7).</param>
public sealed record TransactionDto(
    Guid Id,
    Guid AccountId,
    DateOnly Date,
    Guid? PayeeId,
    string? PayeeName,
    string? Label,
    string? Notes,
    decimal Amount,
    bool IsCleared,
    Guid? TransferId,
    Guid? TransferAccountId,
    IReadOnlyList<TransactionLineDto> Lines,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    int Version,
    bool IsReconciled,
    Guid? ReimbursesId,
    decimal ReimbursedAmount,
    Guid? AdvanceId,
    Guid? CreatedBy,
    Guid? UpdatedBy,
    int AttachmentCount);

/// <summary>
/// Liens d'un revenu (B11, B12) : la dépense qu'il rembourse et l'avance qu'il solde. Modifiables même sur une
/// opération rapprochée, car ils ne changent ni son montant ni sa date.
/// </summary>
public sealed record TransactionLinksRequest(Guid? ReimbursesId, Guid? AdvanceId);

public sealed record TransactionPage(IReadOnlyList<TransactionDto> Items, string? NextCursor);

public sealed record ClearRequest(bool IsCleared);

/// <summary>Nouvelle catégorie d'une modification en masse ; null retire la catégorie.</summary>
public sealed record BulkCategory(Guid? CategoryId);

/// <summary>
/// Modification en masse (B10, I4). Seuls les champs renseignés changent. Une opération à laquelle une modification
/// demandée ne s'applique pas (virement, opération ventilée pour la catégorie, opération importée pour le compte)
/// est laissée intacte et comptée comme ignorée.
/// </summary>
/// <param name="PayeeName">Nouveau tiers (créé s'il n'existe pas).</param>
/// <param name="AddTags">Tags ajoutés à toutes les lignes.</param>
/// <param name="RemoveTags">Tags retirés de toutes les lignes.</param>
public sealed record BulkUpdateRequest(
    [property: Required, MinLength(1), MaxLength(BulkUpdateRequest.MaxItems)] IReadOnlyList<Guid> Ids,
    BulkCategory? Category,
    [property: StringLength(100)] string? PayeeName,
    Guid? AccountId,
    bool? IsCleared,
    IReadOnlyList<string>? AddTags,
    IReadOnlyList<string>? RemoveTags)
{
    public const int MaxItems = 500;
}

/// <param name="Updated">Opérations modifiées.</param>
/// <param name="Skipped">Opérations laissées intactes : modification non applicable, ou opération introuvable.</param>
public sealed record BulkUpdateResult(int Updated, int Skipped);

public sealed record TransferRequest(
    Guid FromAccountId,
    Guid ToAccountId,
    DateOnly Date,
    [property: Range(0.01, 999_999_999)] decimal Amount,
    [property: StringLength(200)] string? Label,
    [property: StringLength(2000)] string? Notes,
    Guid? CategoryId);

public sealed record TransferDto(
    Guid TransferId,
    Guid FromTransactionId,
    Guid ToTransactionId,
    Guid FromAccountId,
    Guid ToAccountId,
    DateOnly Date,
    decimal Amount,
    string? Label,
    string? Notes,
    Guid? CategoryId,
    int Version);
