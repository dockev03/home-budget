using HomeBudget.Api.Domain.Recurrences;
using HomeBudget.Api.Features.Accounts;
using HomeBudget.Api.Features.Periods;
using HomeBudget.Api.Infrastructure.Data;

using Microsoft.EntityFrameworkCore;

namespace HomeBudget.Api.Features.Calendar;

/// <summary>État d'une échéance dans le calendrier (E8).</summary>
public enum CalendarEntryStatus
{
    /// <summary>Prévue, pas encore traitée.</summary>
    Upcoming,

    /// <summary>À valider (E5) : en attente de confirmation, éventuellement reportée.</summary>
    Pending,

    /// <summary>Opération créée.</summary>
    Generated,

    /// <summary>Remplacée par une opération importée (E6).</summary>
    Matched,

    /// <summary>Sautée : elle n'aura pas lieu.</summary>
    Skipped,
}

/// <summary>
/// Calendrier des échéances d'une période (E8) : celles déjà traitées (générées, rapprochées, sautées, à valider)
/// et celles à venir, à leur date d'effet (report au jour ouvré E9 et report manuel compris). Les échéances
/// antérieures à la création de la récurrence, jamais générées, n'y figurent pas.
/// </summary>
public static class CalendarEndpoints
{
    public static IEndpointRouteBuilder MapCalendarEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/calendar/{periodKey}", GetAsync).WithTags("Recurrences")
            .Produces<CalendarDto>().ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Échéances des récurrences de la période (clé AAAA-MM), jour par jour, avec leur état.");
        return app;
    }

    /// <param name="Date">Date d'effet : la date prévue, reportée au jour ouvré ou à la date choisie.</param>
    /// <param name="ScheduledDate">Date prévue par la récurrence.</param>
    /// <param name="Label">Libellé, numéroté pour un paiement en N fois.</param>
    /// <param name="Amount">Montant signé ; positif pour un virement. Pour une opération créée, celui de l'opération.</param>
    /// <param name="TransactionId">Opération créée ou rapprochée, si elle existe encore.</param>
    public sealed record CalendarEntryDto(
        Guid RecurrenceId,
        Guid? OccurrenceId,
        DateOnly Date,
        DateOnly ScheduledDate,
        string Label,
        Guid AccountId,
        Guid? ToAccountId,
        Guid? CategoryId,
        string? PayeeName,
        decimal Amount,
        CalendarEntryStatus Status,
        Guid? TransactionId,
        bool IsSubscription);

    /// <param name="Income">Entrées des échéances non sautées (hors virements).</param>
    /// <param name="Expenses">Sorties des échéances non sautées (hors virements), en positif.</param>
    public sealed record CalendarDto(PeriodEndpoints.PeriodDto Period, decimal Income, decimal Expenses, List<CalendarEntryDto> Entries);

    private static async Task<IResult> GetAsync(string periodKey, AppDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var calculator = await PeriodEndpoints.CalculatorAsync(db);
        if (!calculator.TryParse(periodKey, out var period))
        {
            return PeriodEndpoints.UnknownPeriod();
        }

        var recurrences = await db.Recurrences.AsNoTracking().Include(r => r.Occurrences)
            .Where(r => r.StartDate <= period.End.AddDays(BusinessDays.MaxShiftDays))
            .ToListAsync(ct);
        var payeeIds = recurrences.Select(r => r.PayeeId).OfType<Guid>().Distinct().ToList();
        var payees = await db.Payees.AsNoTracking().Where(p => payeeIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Name, ct);

        var entries = new List<(CalendarEntryDto Entry, Guid? TransactionId)>();
        foreach (var r in recurrences)
        {
            CalendarEntryDto Entry(int index, DateOnly scheduled, DateOnly date, RecurrenceOccurrence? o) => new(
                r.Id, o?.Id, date, scheduled, RecurrenceSchedule.Label(r.Label, r.IsInstallment, index, r.Count),
                r.AccountId, r.ToAccountId, r.CategoryId, r.PayeeId is { } p ? payees.GetValueOrDefault(p) : null,
                r.Amount, o is null ? CalendarEntryStatus.Upcoming : Status(o.Status), o?.TransactionId, r.IsSubscription);

            var known = r.Occurrences.ToDictionary(o => o.ScheduledDate);
            foreach (var o in r.Occurrences.Where(o => period.Contains(o.DueDate)))
            {
                entries.Add((Entry(o.Index, o.ScheduledDate, o.DueDate, o), o.TransactionId));
            }
            foreach (var s in r.Rule.Between(period.Start, period.End))
            {
                if (!known.ContainsKey(s.Date) && s.Date >= r.GenerateFrom)
                {
                    entries.Add((Entry(s.Index, s.Date, s.Date, null), null));
                }
            }
        }

        // Une opération créée ou rapprochée affiche son montant réel (ajusté à la confirmation, ou celui du relevé).
        var transactionIds = entries.Select(e => e.TransactionId).OfType<Guid>().ToList();
        var amounts = await db.Transactions.AsNoTracking().Where(t => transactionIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => t.Amount, ct);
        var result = entries
            .Select(e => e.TransactionId is { } id && amounts.TryGetValue(id, out var amount)
                ? e.Entry with { Amount = e.Entry.ToAccountId is null ? amount : Math.Abs(amount) }
                : e.Entry with { TransactionId = e.TransactionId is { } t && amounts.ContainsKey(t) ? t : null })
            .OrderBy(e => e.Date).ThenBy(e => e.Label, StringComparer.CurrentCulture)
            .ToList();
        var counted = result.Where(e => e.Status != CalendarEntryStatus.Skipped && e.ToAccountId is null).ToList();
        return TypedResults.Ok(new CalendarDto(
            PeriodEndpoints.ToDto(calculator, period, clock),
            counted.Where(e => e.Amount > 0).Sum(e => e.Amount),
            -counted.Where(e => e.Amount < 0).Sum(e => e.Amount),
            result));
    }

    private static CalendarEntryStatus Status(OccurrenceStatus status) => status switch
    {
        OccurrenceStatus.Pending => CalendarEntryStatus.Pending,
        OccurrenceStatus.Generated => CalendarEntryStatus.Generated,
        OccurrenceStatus.Matched => CalendarEntryStatus.Matched,
        _ => CalendarEntryStatus.Skipped,
    };
}
