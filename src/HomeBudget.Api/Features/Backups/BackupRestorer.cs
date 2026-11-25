using HomeBudget.Api.Domain;
using HomeBudget.Api.Domain.Budgets;
using HomeBudget.Api.Domain.Categories;
using HomeBudget.Api.Domain.Payees;
using HomeBudget.Api.Domain.Recurrences;
using HomeBudget.Api.Domain.Settings;
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
        var settings = await db.HouseholdSettings.AsNoTracking().SingleAsync(ct);
        var accounts = await db.Accounts.AsNoTracking().OrderBy(a => a.Id).ToListAsync(ct);
        var categories = await db.Categories.AsNoTracking().OrderBy(c => c.Id).ToListAsync(ct);
        var payees = await db.Payees.AsNoTracking().OrderBy(p => p.Id).ToListAsync(ct);
        var budgets = await db.CategoryBudgets.AsNoTracking().OrderBy(b => b.Id).ToListAsync(ct);
        var transactions = await db.Transactions.AsNoTracking().Include(t => t.Lines).OrderBy(t => t.Id).ToListAsync(ct);
        var recurrences = await db.Recurrences.AsNoTracking().OrderBy(r => r.Id).ToListAsync(ct);
        var occurrences = await db.RecurrenceOccurrences.AsNoTracking().OrderBy(o => o.Id).ToListAsync(ct);

        return new BackupDocument(
            BackupDocument.FormatName,
            BackupDocument.CurrentVersion,
            clock.GetUtcNow(),
            new BackupSettings(
                settings.Id, settings.PeriodMode, settings.PeriodStartDay, settings.SafetyThreshold, settings.ForecastIncludesIncome,
                settings.BudgetWarningPercent, settings.BudgetAlertPercent, settings.DefaultCategoriesCreated, AuditOf(settings)),
            [.. accounts.Select(a => new BackupAccount(
                a.Id, a.Name, a.Type, a.InitialBalance, a.InitialDate, a.OwnerId, a.Visibility, a.Color, a.Icon,
                a.IncludeInForecast, a.IncludeInNetWorth, a.OverdraftThreshold, a.IsArchived, a.SortOrder, AuditOf(a)))],
            [.. categories.Select(c => new BackupCategory(
                c.Id, c.ParentId, c.Name, c.Kind, c.Color, c.Icon, c.SortOrder, c.IsArchived, AuditOf(c)))],
            [.. payees.Select(p => new BackupPayee(p.Id, p.Name, AuditOf(p)))],
            [.. budgets.Select(b => new BackupBudget(b.Id, b.CategoryId, b.Mode, b.Value, AuditOf(b)))],
            [.. transactions.Select(t => new BackupTransaction(
                t.Id, t.AccountId, t.Date, t.PayeeId, t.Label, t.Notes, t.IsCleared, t.TransferId,
                [.. t.Lines.OrderBy(l => l.SortOrder).Select(l => new BackupTransactionLine(l.Id, l.CategoryId, l.Amount, l.Memo))],
                AuditOf(t)))],
            [.. recurrences.Select(r => new BackupRecurrence(
                r.Id, r.Label, r.AccountId, r.ToAccountId, r.PayeeId, r.CategoryId, r.Amount, r.Notes, r.Mode, r.StartDate,
                r.Unit, r.Interval, r.End, r.EndDate, r.Count, r.IsInstallment, r.GenerateFrom, AuditOf(r)))],
            [.. occurrences.Select(o => new BackupOccurrence(
                o.Id, o.RecurrenceId, o.Index, o.ScheduledDate, o.DueDate, o.Status, o.TransactionId, AuditOf(o)))]);
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

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        // Ordre imposé par les clés étrangères : des dépendants vers les éléments référencés.
        await db.RecurrenceOccurrences.ExecuteDeleteAsync(ct);
        await db.Recurrences.ExecuteDeleteAsync(ct);
        await db.TransactionLines.ExecuteDeleteAsync(ct);
        await db.Transactions.ExecuteDeleteAsync(ct);
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
                Visibility = a.Visibility,
                Color = a.Color,
                Icon = a.Icon,
                IncludeInForecast = a.IncludeInForecast,
                IncludeInNetWorth = a.IncludeInNetWorth,
                OverdraftThreshold = a.OverdraftThreshold,
                IsArchived = a.IsArchived,
                SortOrder = a.SortOrder,
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
            }, t.Audit);
            entity.SetLines(t.Lines!.OfType<BackupTransactionLine>().Select(l => new TransactionLine
            {
                Id = l.Id,
                CategoryId = l.CategoryId,
                Amount = l.Amount,
                Memo = l.Memo,
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
