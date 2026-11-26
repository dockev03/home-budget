using HomeBudget.Api.Domain.Accounts;
using HomeBudget.Api.Domain.Budgets;
using HomeBudget.Api.Domain.Categories;
using HomeBudget.Api.Domain.Imports;
using HomeBudget.Api.Domain.Recurrences;
using HomeBudget.Api.Domain.Savings;
using HomeBudget.Api.Domain.Scenarios;
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
    List<BackupOccurrence?>? Occurrences,
    List<BackupImportProfile?>? ImportProfiles = null,
    List<BackupImportBatch?>? ImportBatches = null,
    List<BackupTag?>? Tags = null,
    List<BackupLabelRule?>? LabelRules = null,
    List<BackupReconciliation?>? Reconciliations = null,
    List<BackupAdvance?>? Advances = null,
    List<BackupPeriodBudget?>? PeriodBudgets = null,
    List<BackupProvision?>? Provisions = null,
    List<BackupTransactionChange?>? TransactionChanges = null,
    List<BackupScenario?>? Scenarios = null,
    List<BackupScenarioEvent?>? ScenarioEvents = null,
    List<BackupValuation?>? Valuations = null,
    List<BackupSavingsGoal?>? SavingsGoals = null)
{
    public const string FormatName = "home-budget-backup";

    public const int CurrentVersion = 7;

    /// <summary>Version 1 : sans profils ni historique d'import (étapes 1 à 4), toujours restaurable.</summary>
    public const int MinVersion = 1;

    /// <summary>Version 2 : profils et historique d'import (étape 5), sans tags ni règles apprises.</summary>
    public const int ImportsVersion = 2;

    /// <summary>Version 3 : tags des lignes et règles apprises (étape 6).</summary>
    public const int TagsVersion = 3;

    /// <summary>Version 4 : rapprochements, avances, liens de remboursement et abonnements (étape 7).</summary>
    public const int ReconciliationsVersion = 4;

    /// <summary>Version 5 : budgets par période, provisions, historique des opérations, report au jour ouvré (étape 9).</summary>
    public const int ProvisionsVersion = 5;

    /// <summary>Version 6 : scénarios « et si… » et stratégie de prévision (étape 10).</summary>
    public const int ScenariosVersion = 6;

    /// <summary>Version 7 : valorisations, objectifs d'épargne et épargne de précaution (étape 11).</summary>
    public const int SavingsVersion = 7;
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
    BackupAudit? Audit,
    ForecastStrategy ForecastStrategy = ForecastStrategy.Budget,
    int ForecastAverageMonths = HouseholdSettings.DefaultForecastAverageMonths,
    int EmergencyFundAverageMonths = HouseholdSettings.DefaultForecastAverageMonths);

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
    BackupAudit? Audit,
    bool? IsEmergencyFund = null);

/// <summary>Valorisation d'un compte d'investissement (A7).</summary>
public sealed record BackupValuation(Guid Id, Guid AccountId, DateOnly Date, decimal Value, string? Note, BackupAudit? Audit);

/// <summary>Objectif d'épargne (J1).</summary>
public sealed record BackupSavingsGoal(
    Guid Id,
    string? Name,
    decimal TargetAmount,
    DateOnly? TargetDate,
    Guid? AccountId,
    Guid? CategoryId,
    DateOnly StartDate,
    string? Color,
    string? Icon,
    bool IsArchived,
    BackupAudit? Audit);

public sealed record BackupCategory(
    Guid Id, Guid? ParentId, string? Name, CategoryKind Kind, string? Color, string? Icon, int SortOrder, bool IsArchived, BackupAudit? Audit);

public sealed record BackupPayee(Guid Id, string? Name, BackupAudit? Audit);

public sealed record BackupBudget(Guid Id, Guid CategoryId, BudgetMode Mode, decimal Value, BackupAudit? Audit);

public sealed record BackupPeriodBudget(Guid Id, Guid CategoryId, string? PeriodKey, BudgetMode Mode, decimal Value, BackupAudit? Audit);

public sealed record BackupProvision(
    Guid Id, string? Label, Guid CategoryId, decimal Amount, DateOnly DueDate, bool IsAnnual, DateOnly StartDate, BackupAudit? Audit);

/// <summary>Modification d'une opération (B13). Le nom de l'auteur n'est pas sauvegardé : seul son identifiant.</summary>
/// <param name="Changes">Champs modifiés, au format JSON de l'historique (B13).</param>
public sealed record BackupTransactionChange(Guid Id, Guid TransactionId, DateTimeOffset At, Guid? UserId, string? Changes);

/// <summary>Scénario « et si… » (F5).</summary>
public sealed record BackupScenario(Guid Id, string? Name, bool IsActive, BackupAudit? Audit);

/// <summary>Événement fictif d'un scénario (F5).</summary>
public sealed record BackupScenarioEvent(
    Guid Id,
    Guid ScenarioId,
    string? Label,
    Guid AccountId,
    Guid? CategoryId,
    decimal Amount,
    DateOnly Date,
    ScenarioEventFrequency Frequency,
    DateOnly? EndDate,
    BackupAudit? Audit);

public sealed record BackupTransactionLine(Guid Id, Guid? CategoryId, decimal Amount, string? Memo, List<Guid>? TagIds = null);

public sealed record BackupTag(Guid Id, string? Name, BackupAudit? Audit);

public sealed record BackupLabelRule(Guid Id, string? Key, Guid? PayeeId, Guid? CategoryId, BackupAudit? Audit);

public sealed record BackupReconciliation(Guid Id, Guid AccountId, DateOnly Date, decimal StatementBalance, BackupAudit? Audit);

public sealed record BackupAdvance(Guid Id, string? Label, decimal ExpectedAmount, DateOnly Date, string? Notes, bool IsClosed, BackupAudit? Audit);

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
    BackupAudit? Audit,
    string? RawLabel = null,
    Guid? ImportBatchId = null,
    string? ImportHash = null,
    Guid? ReconciliationId = null,
    Guid? ReimbursesId = null,
    Guid? AdvanceId = null);

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
    BackupAudit? Audit,
    decimal Tolerance = 0,
    ToleranceUnit ToleranceUnit = ToleranceUnit.Amount,
    bool IsSubscription = false,
    BusinessDayShift BusinessDayShift = BusinessDayShift.None);

public sealed record BackupOccurrence(
    Guid Id,
    Guid RecurrenceId,
    int Index,
    DateOnly ScheduledDate,
    DateOnly DueDate,
    OccurrenceStatus Status,
    Guid? TransactionId,
    BackupAudit? Audit);

public sealed record BackupImportProfile(
    Guid Id,
    string? Name,
    char Delimiter,
    string? DateFormat,
    char DecimalSeparator,
    int DateColumn,
    int LabelColumn,
    int? AmountColumn,
    int? DebitColumn,
    int? CreditColumn,
    BackupAudit? Audit);

public sealed record BackupImportBatch(
    Guid Id, Guid AccountId, string? FileName, ImportFormat Format, int Created, int Matched, int Duplicates, BackupAudit? Audit);

/// <summary>Nombre d'éléments restaurés, et nom de l'instantané fait juste avant.</summary>
public sealed record RestoreResultDto(
    int Accounts, int Categories, int Payees, int Budgets, int Transactions, int Recurrences, int Occurrences, string Snapshot);
