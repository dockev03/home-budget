using System.ComponentModel.DataAnnotations;

namespace HomeBudget.Api.Features.Transactions;

public sealed record TransactionLineRequest(
    Guid? CategoryId,
    [property: Range(-999_999_999, 999_999_999)] decimal Amount,
    [property: StringLength(200)] string? Memo);

public sealed record TransactionRequest(
    Guid AccountId,
    DateOnly Date,
    [property: StringLength(100)] string? PayeeName,
    [property: StringLength(200)] string? Label,
    [property: StringLength(2000)] string? Notes,
    bool? IsCleared,
    [property: Required, MinLength(1), MaxLength(50)] IReadOnlyList<TransactionLineRequest> Lines);

public sealed record TransactionLineDto(Guid Id, Guid? CategoryId, decimal Amount, string? Memo);

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
    int Version);

public sealed record TransactionPage(IReadOnlyList<TransactionDto> Items, string? NextCursor);

public sealed record ClearRequest(bool IsCleared);

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
