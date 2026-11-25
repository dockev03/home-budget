using HomeBudget.Api.Domain.Accounts;
using HomeBudget.Api.Domain.Budgets;
using HomeBudget.Api.Domain.Categories;
using HomeBudget.Api.Domain.Recurrences;
using HomeBudget.Api.Domain.Settings;

namespace HomeBudget.Api.Features.Backups;

/// <summary>
/// Sauvegarde complète des données du foyer (H6), au format JSON versionné. Elle ne contient ni les comptes
/// utilisateurs, ni les mots de passe, ni les secrets de double authentification : seuls leurs identifiants
/// apparaissent, comme auteurs ou propriétaires.
/// </summary>
/// <remarks>Les listes sont nullables pour que la validation signale un fichier incomplet au lieu de l'accepter vide.</remarks>
public sealed record BackupDocument(
    string? Format,
    int FormatVersion,
    DateTimeOffset ExportedAt,
    BackupSettings? Settings,
    List<BackupAccount?>? Accounts,
    List<BackupCategory?>? Categories,
    List<BackupPayee?>? Payees,
    List<BackupBudget?>? Budgets,
    List<BackupTransaction?>? Transactions,
    List<BackupRecurrence?>? Recurrences,
    List<BackupOccurrence?>? Occurrences)
{
    public const string FormatName = "home-budget-backup";

    public const int CurrentVersion = 1;
}

/// <summary>Traçabilité (K2) et version de concurrence, conservées à la restauration.</summary>
public sealed record BackupAudit(DateTimeOffset CreatedAt, Guid? CreatedBy, DateTimeOffset UpdatedAt, Guid? UpdatedBy, int Version);

public sealed record BackupSettings(
    Guid Id,
    PeriodMode PeriodMode,
    int PeriodStartDay,
    decimal SafetyThreshold,
    bool ForecastIncludesIncome,
    int BudgetWarningPercent,
    int BudgetAlertPercent,
    bool DefaultCategoriesCreated,
    BackupAudit? Audit);

public sealed record BackupAccount(
    Guid Id,
    string? Name,
    AccountType Type,
    decimal InitialBalance,
    DateOnly InitialDate,
    Guid? OwnerId,
    AccountVisibility Visibility,
    string? Color,
    string? Icon,
    bool IncludeInForecast,
    bool IncludeInNetWorth,
    decimal OverdraftThreshold,
    bool IsArchived,
    int SortOrder,
    BackupAudit? Audit);

public sealed record BackupCategory(
    Guid Id, Guid? ParentId, string? Name, CategoryKind Kind, string? Color, string? Icon, int SortOrder, bool IsArchived, BackupAudit? Audit);

public sealed record BackupPayee(Guid Id, string? Name, BackupAudit? Audit);

public sealed record BackupBudget(Guid Id, Guid CategoryId, BudgetMode Mode, decimal Value, BackupAudit? Audit);

public sealed record BackupTransactionLine(Guid Id, Guid? CategoryId, decimal Amount, string? Memo);

public sealed record BackupTransaction(
    Guid Id,
    Guid AccountId,
    DateOnly Date,
    Guid? PayeeId,
    string? Label,
    string? Notes,
    bool IsCleared,
    Guid? TransferId,
    List<BackupTransactionLine?>? Lines,
    BackupAudit? Audit);

public sealed record BackupRecurrence(
    Guid Id,
    string? Label,
    Guid AccountId,
    Guid? ToAccountId,
    Guid? PayeeId,
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
    DateOnly GenerateFrom,
    BackupAudit? Audit);

public sealed record BackupOccurrence(
    Guid Id,
    Guid RecurrenceId,
    int Index,
    DateOnly ScheduledDate,
    DateOnly DueDate,
    OccurrenceStatus Status,
    Guid? TransactionId,
    BackupAudit? Audit);

/// <summary>Nombre d'éléments restaurés, et nom de l'instantané fait juste avant.</summary>
public sealed record RestoreResultDto(
    int Accounts, int Categories, int Payees, int Budgets, int Transactions, int Recurrences, int Occurrences, string Snapshot);
