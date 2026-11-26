using System.ComponentModel.DataAnnotations;

using HomeBudget.Api.Domain.Recurrences;

namespace HomeBudget.Api.Features.Recurrences;

/// <param name="ToAccountId">Compte de destination : la récurrence est alors un virement (montant positif).</param>
/// <param name="Amount">Montant signé (négatif pour une dépense) ; positif pour un virement.</param>
/// <param name="IsInstallment">Paiement en N fois : fin après <paramref name="Count"/> échéances, libellé numéroté.</param>
/// <param name="Tolerance">Écart de montant admis pour rapprocher une opération importée (E7) ; 0 par défaut.</param>
/// <param name="ToleranceUnit">Unité de la tolérance : euros (par défaut) ou pourcentage du montant.</param>
/// <param name="IsSubscription">Abonnement (E10) : compté dans la vue « Abonnements ».</param>
public sealed record RecurrenceRequest(
    [property: Required, StringLength(200, MinimumLength = 1)] string Label,
    Guid AccountId,
    Guid? ToAccountId,
    [property: StringLength(100)] string? PayeeName,
    Guid? CategoryId,
    [property: Range(-999_999_999, 999_999_999)] decimal Amount,
    [property: StringLength(2000)] string? Notes,
    RecurrenceMode Mode,
    DateOnly StartDate,
    FrequencyUnit Unit,
    int Interval,
    RecurrenceEnd End,
    DateOnly? EndDate,
    int? Count,
    bool IsInstallment,
    [property: Range(0, 999_999_999)] decimal? Tolerance = null,
    ToleranceUnit? ToleranceUnit = null,
    bool IsSubscription = false);

/// <param name="NextDate">Prochaine échéance attendue (une échéance à valider en retard compte) ; null si terminée.</param>
/// <param name="PendingCount">Échéances en attente de validation.</param>
public sealed record RecurrenceDto(
    Guid Id,
    string Label,
    Guid AccountId,
    Guid? ToAccountId,
    Guid? PayeeId,
    string? PayeeName,
    Guid? CategoryId,
    decimal Amount,
    string? Notes,
    RecurrenceMode Mode,
    DateOnly StartDate,
    FrequencyUnit Unit,
    int Interval,
    RecurrenceEnd End,
    DateOnly? EndDate,
    int? Count,
    bool IsInstallment,
    decimal Tolerance,
    ToleranceUnit ToleranceUnit,
    DateOnly? NextDate,
    int PendingCount,
    int Version,
    bool IsSubscription);

/// <param name="Label">Libellé de l'échéance, numéroté pour un paiement en N fois.</param>
/// <param name="ScheduledDate">Date prévue par la récurrence.</param>
/// <param name="DueDate">Date d'effet : la date prévue, ou celle du report.</param>
public sealed record PendingOccurrenceDto(
    Guid Id,
    Guid RecurrenceId,
    string Label,
    Guid AccountId,
    Guid? ToAccountId,
    string? PayeeName,
    Guid? CategoryId,
    decimal Amount,
    DateOnly ScheduledDate,
    DateOnly DueDate);

/// <param name="Amount">Montant réel, s'il diffère du montant prévu (même convention de signe).</param>
/// <param name="Date">Date de l'opération (par défaut, la date d'effet de l'échéance).</param>
public sealed record ConfirmOccurrenceRequest(
    [property: Range(-999_999_999, 999_999_999)] decimal? Amount,
    DateOnly? Date);

public sealed record PostponeOccurrenceRequest(DateOnly Date);

/// <param name="TransactionId">Opération créée (la sortie, pour un virement).</param>
public sealed record OccurrenceResultDto(Guid Id, OccurrenceStatus Status, DateOnly DueDate, Guid? TransactionId);
