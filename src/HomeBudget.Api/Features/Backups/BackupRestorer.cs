using HomeBudget.Api.Domain;
using HomeBudget.Api.Domain.Advances;
using HomeBudget.Api.Domain.Budgets;
using HomeBudget.Api.Domain.Categories;
using HomeBudget.Api.Domain.Imports;
using HomeBudget.Api.Domain.Payees;
using HomeBudget.Api.Domain.Reconciliations;
using HomeBudget.Api.Domain.Recurrences;
using HomeBudget.Api.Domain.Rules;
using HomeBudget.Api.Domain.Savings;
using HomeBudget.Api.Domain.Scenarios;
using HomeBudget.Api.Domain.Settings;
using HomeBudget.Api.Domain.Tags;
using HomeBudget.Api.Domain.Transactions;
using HomeBudget.Api.Infrastructure.Data;

using Microsoft.EntityFrameworkCore;

namespace HomeBudget.Api.Features.Backups;

using Account = HomeBudget.Api.Domain.Accounts.Account;

/// <summary>Lecture des données vers une sauvegarde (H6), et restauration d'une sauvegarde validée.</summary>
public sealed class BackupRestorer(AppDbContext db, TimeProvider clock)
{
    public async Task<BackupDocument> ExportAsync(CancellationToken ct)
    {
        // Sauvegarde complète (administrateur ou tâche de fond) : les comptes privés de chacun en font partie.
        db.IgnoreVisibility = true;
        var settings = await db.HouseholdSettings.AsNoTracking().SingleAsync(ct);
        var accounts = await db.Accounts.AsNoTracking().OrderBy(a => a.Id).ToListAsync(ct);
        var categories = await db.Categories.AsNoTracking().OrderBy(c => c.Id).ToListAsync(ct);
        var payees = await db.Payees.AsNoTracking().OrderBy(p => p.Id).ToListAsync(ct);
        var budgets = await db.CategoryBudgets.AsNoTracking().OrderBy(b => b.Id).ToListAsync(ct);
        var transactions = await db.Transactions.AsNoTracking().Include(t => t.Lines).ThenInclude(l => l.Tags).OrderBy(t => t.Id).ToListAsync(ct);
        var recurrences = await db.Recurrences.AsNoTracking().OrderBy(r => r.Id).ToListAsync(ct);
        var occurrences = await db.RecurrenceOccurrences.AsNoTracking().OrderBy(o => o.Id).ToListAsync(ct);
        var profiles = await db.ImportProfiles.AsNoTracking().OrderBy(p => p.Id).ToListAsync(ct);
        var batches = await db.ImportBatches.AsNoTracking().OrderBy(b => b.Id).ToListAsync(ct);
        var tags = await db.Tags.AsNoTracking().OrderBy(t => t.Id).ToListAsync(ct);
        var rules = await db.LabelRules.AsNoTracking().OrderBy(r => r.Id).ToListAsync(ct);
        var reconciliations = await db.Reconciliations.AsNoTracking().OrderBy(r => r.Id).ToListAsync(ct);
        var advances = await db.Advances.AsNoTracking().OrderBy(a => a.Id).ToListAsync(ct);
        var periodBudgets = await db.PeriodBudgets.AsNoTracking().OrderBy(b => b.Id).ToListAsync(ct);
        var provisions = await db.Provisions.AsNoTracking().OrderBy(p => p.Id).ToListAsync(ct);
        var changes = await db.TransactionChanges.AsNoTracking().OrderBy(c => c.Id).ToListAsync(ct);
        var scenarios = await db.Scenarios.AsNoTracking().OrderBy(s => s.Id).ToListAsync(ct);
        var scenarioEvents = await db.ScenarioEvents.AsNoTracking().OrderBy(e => e.Id).ToListAsync(ct);
        var valuations = await db.AccountValuations.AsNoTracking().OrderBy(v => v.Id).ToListAsync(ct);
        var goals = await db.SavingsGoals.AsNoTracking().OrderBy(g => g.Id).ToListAsync(ct);

        return new BackupDocument(
            BackupDocument.FormatName,
            BackupDocument.CurrentVersion,
            clock.GetUtcNow(),
            new BackupSettings(
                settings.Id, settings.PeriodMode, settings.PeriodStartDay, settings.SafetyThreshold, settings.ForecastIncludesIncome,
                settings.BudgetWarningPercent, settings.BudgetAlertPercent, settings.DefaultCategoriesCreated, AuditOf(settings),
                settings.ForecastStrategy, settings.ForecastAverageMonths, settings.EmergencyFundAverageMonths),
            [.. accounts.Select(a => new BackupAccount(
                a.Id, a.Name, a.Type, a.InitialBalance, a.InitialDate, a.OwnerId, a.Visibility, a.Color, a.Icon,
                a.IncludeInForecast, a.IncludeInNetWorth, a.OverdraftThreshold, a.IsArchived, a.SortOrder, AuditOf(a), a.IsEmergencyFund))],
            [.. categories.Select(c => new BackupCategory(
                c.Id, c.ParentId, c.Name, c.Kind, c.Color, c.Icon, c.SortOrder, c.IsArchived, AuditOf(c)))],
            [.. payees.Select(p => new BackupPayee(p.Id, p.Name, AuditOf(p)))],
            [.. budgets.Select(b => new BackupBudget(b.Id, b.CategoryId, b.Mode, b.Value, AuditOf(b)))],
            [.. transactions.Select(t => new BackupTransaction(
                t.Id, t.AccountId, t.Date, t.PayeeId, t.Label, t.Notes, t.IsCleared, t.TransferId,
                [.. t.Lines.OrderBy(l => l.SortOrder).Select(l => new BackupTransactionLine(
                    l.Id, l.CategoryId, l.Amount, l.Memo, [.. l.Tags.Select(g => g.Id).Order()]))],
                AuditOf(t), t.RawLabel, t.ImportBatchId, t.ImportHash, t.ReconciliationId, t.ReimbursesId, t.AdvanceId))],
            [.. recurrences.Select(r => new BackupRecurrence(
                r.Id, r.Label, r.AccountId, r.ToAccountId, r.PayeeId, r.CategoryId, r.Amount, r.Notes, r.Mode, r.StartDate,
                r.Unit, r.Interval, r.End, r.EndDate, r.Count, r.IsInstallment, r.GenerateFrom, AuditOf(r), r.Tolerance, r.ToleranceUnit, r.IsSubscription, r.BusinessDayShift))],
            [.. occurrences.Select(o => new BackupOccurrence(
                o.Id, o.RecurrenceId, o.Index, o.ScheduledDate, o.DueDate, o.Status, o.TransactionId, AuditOf(o)))],
            [.. profiles.Select(p => new BackupImportProfile(
                p.Id, p.Name, p.Delimiter, p.DateFormat, p.DecimalSeparator, p.DateColumn, p.LabelColumn,
                p.AmountColumn, p.DebitColumn, p.CreditColumn, AuditOf(p)))],
            [.. batches.Select(b => new BackupImportBatch(
                b.Id, b.AccountId, b.FileName, b.Format, b.Created, b.Matched, b.Duplicates, AuditOf(b)))],
            [.. tags.Select(t => new BackupTag(t.Id, t.Name, AuditOf(t)))],
            [.. rules.Select(r => new BackupLabelRule(r.Id, r.Key, r.PayeeId, r.CategoryId, AuditOf(r)))],
            [.. reconciliations.Select(r => new BackupReconciliation(r.Id, r.AccountId, r.Date, r.StatementBalance, AuditOf(r)))],
            [.. advances.Select(a => new BackupAdvance(a.Id, a.Label, a.ExpectedAmount, a.Date, a.Notes, a.IsClosed, AuditOf(a)))],
            [.. periodBudgets.Select(b => new BackupPeriodBudget(b.Id, b.CategoryId, b.PeriodKey, b.Mode, b.Value, AuditOf(b)))],
            [.. provisions.Select(p => new BackupProvision(p.Id, p.Label, p.CategoryId, p.Amount, p.DueDate, p.IsAnnual, p.StartDate, AuditOf(p)))],
            [.. changes.Select(c => new BackupTransactionChange(c.Id, c.TransactionId, c.At, c.UserId, c.Changes))],
            [.. scenarios.Select(s => new BackupScenario(s.Id, s.Name, s.IsActive, AuditOf(s)))],
            [.. scenarioEvents.Select(e => new BackupScenarioEvent(
                e.Id, e.ScenarioId, e.Label, e.AccountId, e.CategoryId, e.Amount, e.Date, e.Frequency, e.EndDate, AuditOf(e)))],
            [.. valuations.Select(v => new BackupValuation(v.Id, v.AccountId, v.Date, v.Value, v.Note, AuditOf(v)))],
            [.. goals.Select(g => new BackupSavingsGoal(
                g.Id, g.Name, g.TargetAmount, g.TargetDate, g.AccountId, g.CategoryId, g.StartDate, g.Color, g.Icon, g.IsArchived, AuditOf(g)))]);
    }

    /// <summary>
    /// Remplace toutes les données du foyer par celles de la sauvegarde, dans une seule transaction : en cas
    /// d'erreur, rien ne change. Les comptes utilisateurs sont conservés ; une référence à un utilisateur
    /// inconnu de cette instance (auteur, propriétaire) est vidée.
    /// </summary>
    /// <param name="document">Sauvegarde déjà validée par <see cref="BackupValidator"/>.</param>
    public async Task RestoreAsync(BackupDocument document, CancellationToken ct)
    {
        var users = (await db.Users.AsNoTracking().Select(u => u.Id).ToListAsync(ct)).ToHashSet();
        Guid? User(Guid? id) => id is { } value && users.Contains(value) ? value : null;
        T Stamp<T>(T entity, BackupAudit? audit)
            where T : AuditableEntity
        {
            entity.CreatedAt = audit!.CreatedAt;
            entity.CreatedBy = User(audit.CreatedBy);
            entity.UpdatedAt = audit.UpdatedAt;
            entity.UpdatedBy = User(audit.UpdatedBy);
            entity.Version = audit.Version;
            return entity;
        }

        // La restauration remplace toute la base, comptes privés compris.
        db.IgnoreVisibility = true;
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        // Ordre imposé par les clés étrangères : des dépendants vers les éléments référencés.
        await db.RecurrenceOccurrences.ExecuteDeleteAsync(ct);
        // Remplacés quelle que soit la version : une sauvegarde antérieure à la v7 n'en contient pas.
        await db.SavingsGoals.ExecuteDeleteAsync(ct);
        await db.AccountValuations.ExecuteDeleteAsync(ct);
        // Remplacés quelle que soit la version : une sauvegarde antérieure à la v6 n'en contient pas.
        await db.ScenarioEvents.ExecuteDeleteAsync(ct);
        await db.Scenarios.ExecuteDeleteAsync(ct);
        // Remplacés quelle que soit la version : une sauvegarde antérieure à la v5 n'en contient pas.
        await db.TransactionChanges.ExecuteDeleteAsync(ct);
        await db.PeriodBudgets.ExecuteDeleteAsync(ct);
        await db.Provisions.ExecuteDeleteAsync(ct);
        // Règles et tags sont remplacés quelle que soit la version : une sauvegarde v1 ou v2 n'en contient pas.
        await db.LabelRules.ExecuteDeleteAsync(ct);
        await db.Recurrences.ExecuteDeleteAsync(ct);
        await db.TransactionLines.ExecuteDeleteAsync(ct);
        await db.Transactions.ExecuteDeleteAsync(ct);
        await db.Tags.ExecuteDeleteAsync(ct);
        // Après les opérations, qui les référencent ; remplacés quelle que soit la version.
        await db.Reconciliations.ExecuteDeleteAsync(ct);
        await db.Advances.ExecuteDeleteAsync(ct);
        await db.ImportBatches.ExecuteDeleteAsync(ct);
        // Une sauvegarde de version 1 ne contient pas de profils : ceux de l'instance sont conservés.
        if (document.ImportProfiles is not null)
        {
            await db.ImportProfiles.ExecuteDeleteAsync(ct);
        }
        await db.CategoryBudgets.ExecuteDeleteAsync(ct);
        await db.Payees.ExecuteDeleteAsync(ct);
        await db.Categories.Where(c => c.ParentId != null).ExecuteDeleteAsync(ct);
        await db.Categories.ExecuteDeleteAsync(ct);
        await db.Accounts.ExecuteDeleteAsync(ct);
        await db.HouseholdSettings.ExecuteDeleteAsync(ct);

        var s = document.Settings!;
        db.HouseholdSettings.Add(Stamp(new HouseholdSettings
        {
            Id = s.Id,
            PeriodMode = s.PeriodMode,
            PeriodStartDay = s.PeriodStartDay,
            SafetyThreshold = s.SafetyThreshold,
            ForecastIncludesIncome = s.ForecastIncludesIncome,
            BudgetWarningPercent = s.BudgetWarningPercent,
            BudgetAlertPercent = s.BudgetAlertPercent,
            DefaultCategoriesCreated = s.DefaultCategoriesCreated,
            ForecastStrategy = s.ForecastStrategy,
            ForecastAverageMonths = s.ForecastAverageMonths,
            EmergencyFundAverageMonths = s.EmergencyFundAverageMonths,
        }, s.Audit));

        foreach (var a in document.Accounts!.OfType<BackupAccount>())
        {
            db.Accounts.Add(Stamp(new Account
            {
                Id = a.Id,
                Name = a.Name!,
                Type = a.Type,
                InitialBalance = a.InitialBalance,
                InitialDate = a.InitialDate,
                OwnerId = User(a.OwnerId),
                Visibility = User(a.OwnerId) is null ? Domain.Accounts.AccountVisibility.Shared : a.Visibility,
                Color = a.Color,
                Icon = a.Icon,
                IncludeInForecast = a.IncludeInForecast,
                IncludeInNetWorth = a.IncludeInNetWorth,
                OverdraftThreshold = a.OverdraftThreshold,
                IsArchived = a.IsArchived,
                SortOrder = a.SortOrder,
                // Avant la v7, l'épargne de précaution suit le type du compte, comme à la migration.
                IsEmergencyFund = a.IsEmergencyFund ?? a.Type == Domain.Accounts.AccountType.Savings,
            }, a.Audit));
        }

        foreach (var c in document.Categories!.OfType<BackupCategory>())
        {
            db.Categories.Add(Stamp(new Category
            {
                Id = c.Id,
                ParentId = c.ParentId,
                Name = c.Name!,
                Kind = c.Kind,
                Color = c.Color,
                Icon = c.Icon,
                SortOrder = c.SortOrder,
                IsArchived = c.IsArchived,
            }, c.Audit));
        }

        foreach (var p in document.Payees!.OfType<BackupPayee>())
        {
            var name = p.Name!.Trim();
            db.Payees.Add(Stamp(new Payee { Id = p.Id, Name = name, NormalizedName = Payee.Normalize(name) }, p.Audit));
        }

        foreach (var b in document.Budgets!.OfType<BackupBudget>())
        {
            db.CategoryBudgets.Add(Stamp(new CategoryBudget { Id = b.Id, CategoryId = b.CategoryId, Mode = b.Mode, Value = b.Value }, b.Audit));
        }

        foreach (var b in (document.ImportBatches ?? []).OfType<BackupImportBatch>())
        {
            db.ImportBatches.Add(Stamp(new ImportBatch
            {
                Id = b.Id,
                AccountId = b.AccountId,
                FileName = b.FileName!,
                Format = b.Format,
                Created = b.Created,
                Matched = b.Matched,
                Duplicates = b.Duplicates,
            }, b.Audit));
        }

        foreach (var p in (document.ImportProfiles ?? []).OfType<BackupImportProfile>())
        {
            db.ImportProfiles.Add(Stamp(new ImportProfile
            {
                Id = p.Id,
                Name = p.Name!.Trim(),
                Delimiter = p.Delimiter,
                DateFormat = p.DateFormat!,
                DecimalSeparator = p.DecimalSeparator,
                DateColumn = p.DateColumn,
                LabelColumn = p.LabelColumn,
                AmountColumn = p.AmountColumn,
                DebitColumn = p.DebitColumn,
                CreditColumn = p.CreditColumn,
            }, p.Audit));
        }

        var tags = new Dictionary<Guid, Tag>();
        foreach (var g in (document.Tags ?? []).OfType<BackupTag>())
        {
            var name = Tag.Clean(g.Name!);
            tags[g.Id] = Stamp(new Tag { Id = g.Id, Name = name, NormalizedName = Tag.Normalize(name) }, g.Audit);
            db.Tags.Add(tags[g.Id]);
        }

        foreach (var r in (document.LabelRules ?? []).OfType<BackupLabelRule>())
        {
            db.LabelRules.Add(Stamp(new LabelRule { Id = r.Id, Key = r.Key!, PayeeId = r.PayeeId, CategoryId = r.CategoryId }, r.Audit));
        }

        foreach (var r in (document.Reconciliations ?? []).OfType<BackupReconciliation>())
        {
            db.Reconciliations.Add(Stamp(new Reconciliation { Id = r.Id, AccountId = r.AccountId, Date = r.Date, StatementBalance = r.StatementBalance }, r.Audit));
        }

        foreach (var a in (document.Advances ?? []).OfType<BackupAdvance>())
        {
            db.Advances.Add(Stamp(new Advance
            {
                Id = a.Id,
                Label = a.Label!.Trim(),
                ExpectedAmount = a.ExpectedAmount,
                Date = a.Date,
                Notes = a.Notes,
                IsClosed = a.IsClosed,
            }, a.Audit));
        }

        foreach (var t in document.Transactions!.OfType<BackupTransaction>())
        {
            var entity = Stamp(new Transaction
            {
                Id = t.Id,
                AccountId = t.AccountId,
                Date = t.Date,
                PayeeId = t.PayeeId,
                Label = t.Label,
                Notes = t.Notes,
                IsCleared = t.IsCleared,
                TransferId = t.TransferId,
                RawLabel = t.RawLabel,
                ImportBatchId = t.ImportBatchId,
                ImportHash = t.ImportHash,
                ReconciliationId = t.ReconciliationId,
                ReimbursesId = t.ReimbursesId,
                AdvanceId = t.AdvanceId,
            }, t.Audit);
            entity.SetLines(t.Lines!.OfType<BackupTransactionLine>().Select(l => new TransactionLine
            {
                Id = l.Id,
                CategoryId = l.CategoryId,
                Amount = l.Amount,
                Memo = l.Memo,
                Tags = [.. (l.TagIds ?? []).Distinct().Select(id => tags[id])],
            }));
            db.Transactions.Add(entity);
        }

        foreach (var r in document.Recurrences!.OfType<BackupRecurrence>())
        {
            db.Recurrences.Add(Stamp(new Recurrence
            {
                Id = r.Id,
                Label = r.Label!,
                AccountId = r.AccountId,
                ToAccountId = r.ToAccountId,
                PayeeId = r.PayeeId,
                CategoryId = r.CategoryId,
                Amount = r.Amount,
                Notes = r.Notes,
                Mode = r.Mode,
                StartDate = r.StartDate,
                Unit = r.Unit,
                Interval = r.Interval,
                End = r.End,
                EndDate = r.EndDate,
                Count = r.Count,
                IsInstallment = r.IsInstallment,
                GenerateFrom = r.GenerateFrom,
                Tolerance = r.Tolerance,
                ToleranceUnit = r.ToleranceUnit,
                IsSubscription = r.IsSubscription,
                BusinessDayShift = r.BusinessDayShift,
            }, r.Audit));
        }

        foreach (var o in document.Occurrences!.OfType<BackupOccurrence>())
        {
            db.RecurrenceOccurrences.Add(Stamp(new RecurrenceOccurrence
            {
                Id = o.Id,
                RecurrenceId = o.RecurrenceId,
                Index = o.Index,
                ScheduledDate = o.ScheduledDate,
                DueDate = o.DueDate,
                Status = o.Status,
                TransactionId = o.TransactionId,
            }, o.Audit));
        }

        foreach (var b in (document.PeriodBudgets ?? []).OfType<BackupPeriodBudget>())
        {
            db.PeriodBudgets.Add(Stamp(new PeriodBudget { Id = b.Id, CategoryId = b.CategoryId, PeriodKey = b.PeriodKey!, Mode = b.Mode, Value = b.Value }, b.Audit));
        }

        foreach (var p in (document.Provisions ?? []).OfType<BackupProvision>())
        {
            db.Provisions.Add(Stamp(new Provision
            {
                Id = p.Id,
                Label = p.Label!.Trim(),
                CategoryId = p.CategoryId,
                Amount = p.Amount,
                DueDate = p.DueDate,
                IsAnnual = p.IsAnnual,
                StartDate = p.StartDate,
            }, p.Audit));
        }

        foreach (var c in (document.TransactionChanges ?? []).OfType<BackupTransactionChange>())
        {
            db.TransactionChanges.Add(new TransactionChange
            {
                Id = c.Id,
                TransactionId = c.TransactionId,
                At = c.At,
                UserId = User(c.UserId),
                Changes = c.Changes!,
            });
        }

        foreach (var sc in (document.Scenarios ?? []).OfType<BackupScenario>())
        {
            db.Scenarios.Add(Stamp(new Scenario { Id = sc.Id, Name = sc.Name!.Trim(), IsActive = sc.IsActive }, sc.Audit));
        }

        foreach (var e in (document.ScenarioEvents ?? []).OfType<BackupScenarioEvent>())
        {
            db.ScenarioEvents.Add(Stamp(new ScenarioEvent
            {
                Id = e.Id,
                ScenarioId = e.ScenarioId,
                Label = e.Label!.Trim(),
                AccountId = e.AccountId,
                CategoryId = e.CategoryId,
                Amount = e.Amount,
                Date = e.Date,
                Frequency = e.Frequency,
                EndDate = e.Frequency == ScenarioEventFrequency.Monthly ? e.EndDate : null,
            }, e.Audit));
        }

        foreach (var v in (document.Valuations ?? []).OfType<BackupValuation>())
        {
            db.AccountValuations.Add(Stamp(new Domain.Accounts.AccountValuation
            {
                Id = v.Id,
                AccountId = v.AccountId,
                Date = v.Date,
                Value = v.Value,
                Note = string.IsNullOrWhiteSpace(v.Note) ? null : v.Note.Trim(),
            }, v.Audit));
        }

        foreach (var g in (document.SavingsGoals ?? []).OfType<BackupSavingsGoal>())
        {
            db.SavingsGoals.Add(Stamp(new SavingsGoal
            {
                Id = g.Id,
                Name = g.Name!.Trim(),
                TargetAmount = g.TargetAmount,
                TargetDate = g.TargetDate,
                AccountId = g.AccountId,
                CategoryId = g.AccountId is null ? g.CategoryId : null,
                StartDate = g.StartDate,
                Color = g.Color,
                Icon = g.Icon,
                IsArchived = g.IsArchived,
            }, g.Audit));
        }

        db.PreserveAuditFields = true;
        try
        {
            await db.SaveChangesAsync(ct);
        }
        finally
        {
            db.PreserveAuditFields = false;
        }
        await transaction.CommitAsync(ct);
    }

    private static BackupAudit AuditOf(AuditableEntity e) => new(e.CreatedAt, e.CreatedBy, e.UpdatedAt, e.UpdatedBy, e.Version);
}
