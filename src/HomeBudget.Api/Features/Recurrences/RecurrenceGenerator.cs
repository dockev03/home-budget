using HomeBudget.Api.Domain.Recurrences;
using HomeBudget.Api.Domain.Transactions;
using HomeBudget.Api.Features.Accounts;
using HomeBudget.Api.Infrastructure.Data;

using Microsoft.EntityFrameworkCore;

namespace HomeBudget.Api.Features.Recurrences;

/// <summary>
/// Traite les échéances échues (E4, E5) : crée l'opération d'une récurrence automatique, ou place l'échéance
/// dans « À valider ». Appelé chaque nuit par <see cref="RecurrenceWorker"/> et après chaque saisie de récurrence.
/// </summary>
public sealed partial class RecurrenceGenerator(AppDbContext db, TimeProvider clock, ILogger<RecurrenceGenerator> logger)
{
    /// <summary>Traite toutes les récurrences ; renvoie le nombre d'échéances traitées.</summary>
    public async Task<int> RunAsync(CancellationToken ct = default)
    {
        var ids = await db.Recurrences.AsNoTracking().Select(r => r.Id).ToListAsync(ct);
        var total = 0;
        foreach (var id in ids)
        {
            total += await RunAsync(id, ct);
        }
        return total;
    }

    /// <summary>
    /// Traite une récurrence. Une génération concurrente de la même échéance se heurte à l'index unique :
    /// l'échéance est alors déjà traitée, et rien n'est créé en double.
    /// </summary>
    public async Task<int> RunAsync(Guid recurrenceId, CancellationToken ct = default)
    {
        db.ChangeTracker.Clear();
        var recurrence = await db.Recurrences.Include(r => r.Occurrences).FirstOrDefaultAsync(r => r.Id == recurrenceId, ct);
        if (recurrence is null)
        {
            return 0;
        }
        var accounts = await db.Accounts.AsNoTracking()
            .Where(a => a.Id == recurrence.AccountId || a.Id == recurrence.ToAccountId)
            .ToDictionaryAsync(a => a.Id, ct);
        // Un compte archivé ne reçoit plus d'opérations : ses échéances attendent sa réactivation.
        if (accounts.Values.Any(a => a.IsArchived) || accounts.Count == 0)
        {
            return 0;
        }

        var due = RecurrenceSchedule.DueOn(recurrence.Rule, recurrence.GenerateFrom, recurrence.States, Balances.Today(clock));
        foreach (var scheduled in due)
        {
            var occurrence = new RecurrenceOccurrence
            {
                RecurrenceId = recurrence.Id,
                Index = scheduled.Index,
                ScheduledDate = scheduled.Date,
                DueDate = scheduled.Date,
                Status = OccurrenceStatus.Pending,
                ExpectedAmount = recurrence.Amount,
            };
            if (recurrence.Mode == RecurrenceMode.Automatic)
            {
                occurrence.Status = OccurrenceStatus.Generated;
                occurrence.TransactionId = CreateTransactions(recurrence, occurrence, scheduled.Date, recurrence.Amount, accounts).Id;
            }
            db.RecurrenceOccurrences.Add(occurrence);
        }
        if (due.Count == 0)
        {
            return 0;
        }
        try
        {
            await db.SaveChangesAsync(ct);
            return due.Count;
        }
        catch (DbUpdateException ex)
        {
            LogConcurrentGeneration(ex, recurrence.Id);
            db.ChangeTracker.Clear();
            return 0;
        }
    }

    /// <summary>
    /// Crée l'opération d'une échéance (deux opérations liées pour un virement) et renvoie celle du compte source.
    /// </summary>
    public Transaction CreateTransactions(
        Recurrence recurrence, RecurrenceOccurrence occurrence, DateOnly date, decimal amount, IReadOnlyDictionary<Guid, Domain.Accounts.Account> accounts)
    {
        var label = RecurrenceSchedule.Label(recurrence.Label, recurrence.IsInstallment, occurrence.Index, recurrence.Count);
        // L'opération ne peut précéder le solde initial du compte.
        var effective = accounts.Values.Select(a => a.InitialDate).Append(date).Max();
        if (recurrence.ToAccountId is not { } toAccountId)
        {
            var transaction = new Transaction
            {
                AccountId = recurrence.AccountId,
                Date = effective,
                PayeeId = recurrence.PayeeId,
                Label = label,
                Notes = recurrence.Notes,
            };
            transaction.SetLines([new TransactionLine { CategoryId = recurrence.CategoryId, Amount = amount }]);
            db.Transactions.Add(transaction);
            return transaction;
        }

        var transferId = Guid.CreateVersion7();
        var from = new Transaction { AccountId = recurrence.AccountId, TransferId = transferId };
        var to = new Transaction { AccountId = toAccountId, TransferId = transferId };
        foreach (var (leg, sign) in new[] { (from, -1m), (to, 1m) })
        {
            leg.Date = effective;
            leg.Label = label;
            leg.Notes = recurrence.Notes;
            // Seule la sortie porte la catégorie d'épargne (R5), comme pour un virement saisi.
            leg.SetLines([new TransactionLine { Amount = sign * amount, CategoryId = sign < 0 ? recurrence.CategoryId : null }]);
        }
        db.Transactions.AddRange(from, to);
        return from;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Échéances de la récurrence {RecurrenceId} déjà traitées par ailleurs.")]
    private partial void LogConcurrentGeneration(Exception exception, Guid recurrenceId);
}
