using HomeBudget.Api.Domain.Budgets;
using HomeBudget.Api.Domain.Notifications;
using HomeBudget.Api.Domain.Recurrences;
using HomeBudget.Api.Domain.Users;
using HomeBudget.Api.Features.Accounts;
using HomeBudget.Api.Features.Budgets;
using HomeBudget.Api.Features.Forecasts;
using HomeBudget.Api.Infrastructure.Configuration;
using HomeBudget.Api.Infrastructure.Data;
using HomeBudget.Api.Infrastructure.Notifications;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HomeBudget.Api.Features.Notifications;

/// <summary>Alerte candidate : une notification, et les clés (objet, période) qu'elle couvre.</summary>
public sealed record Alert(AlertKind Kind, PushMessage Message, IReadOnlyList<(string ObjectKey, string PeriodKey)> Keys);

/// <summary>
/// Alertes push (M1) et résumé par e-mail (M2), calculés pour chaque utilisateur avec ses seuls comptes visibles
/// (R8). Une alerte (type, objet, période) n'est envoyée qu'une fois ; les alertes regroupées (échéances de
/// demain, à valider) ne repartent que si un nouvel objet s'y ajoute.
/// </summary>
public sealed partial class AlertService(
    AppDbContext db,
    ForecastService forecast,
    IBudgetScheduleSource schedule,
    WebPushSender push,
    IEmailSender email,
    IOptions<HomeBudgetOptions> options,
    TimeProvider clock,
    ILogger<AlertService> logger)
{
    /// <summary>Pas de notification la nuit : elles attendent la vérification qui suit l'heure configurée.</summary>
    public const int QuietFromHour = 22;

    public const int RetentionDays = 400;

    public sealed record RunResult(int PushSent, int EmailsSent);

    public async Task<RunResult> RunAsync(CancellationToken ct)
    {
        var now = clock.GetLocalNow();
        var hour = options.Value.Notifications.Hour;
        if (now.Hour < hour || now.Hour >= QuietFromHour)
        {
            return new RunResult(0, 0);
        }

        // Historique des alertes envoyées : au-delà d'un an, plus aucune ne peut se reproduire.
        // SQLite ne compare pas les DateTimeOffset : le tri se fait ici (table de quelques centaines de lignes).
        var horizon = clock.GetUtcNow().AddDays(-RetentionDays);
        var expired = (await db.SentAlerts.AsNoTracking().Select(s => new { s.Id, s.SentAt }).ToListAsync(ct))
            .Where(s => s.SentAt < horizon).Select(s => s.Id).ToList();
        if (expired.Count > 0)
        {
            await db.SentAlerts.Where(s => expired.Contains(s.Id)).ExecuteDeleteAsync(ct);
        }

        var users = await db.Users.AsNoTracking().Where(u => u.IsActive).OrderBy(u => u.Id).ToListAsync(ct);
        var preferences = await db.NotificationPreferences.AsNoTracking().ToDictionaryAsync(p => p.UserId, ct);
        var subscriptions = push.IsConfigured
            ? (await db.PushSubscriptions.AsNoTracking().ToListAsync(ct)).ToLookup(s => s.UserId)
            : Enumerable.Empty<PushSubscription>().ToLookup(s => s.UserId);

        int pushSent = 0, emailsSent = 0;
        foreach (var user in users)
        {
            var preference = preferences.GetValueOrDefault(user.Id) ?? new NotificationPreference { UserId = user.Id };
            db.ActAsViewer = user.Id;
            try
            {
                if (subscriptions[user.Id].Any())
                {
                    pushSent += await NotifyAsync(user, preference, [.. subscriptions[user.Id]], ct);
                }
                if (await SummaryDueAsync(user, preference, now, ct) is { } summary)
                {
                    emailsSent += await SendSummaryAsync(user, preference, summary, ct) ? 1 : 0;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogUserFailed(user.Id, ex);
            }
            finally
            {
                db.ActAsViewer = null;
                db.ChangeTracker.Clear();
            }
        }
        return new RunResult(pushSent, emailsSent);
    }

    /// <summary>Alertes de l'utilisateur, avant filtrage par ses préférences et par les alertes déjà envoyées.</summary>
    public async Task<List<Alert>> ComputeAsync(CancellationToken ct)
    {
        var settings = await db.HouseholdSettings.AsNoTracking().SingleAsync(ct);
        var calculator = Domain.Periods.PeriodCalculator.From(settings);
        var today = Balances.Today(clock);
        var period = calculator.For(today);
        var alerts = new List<Alert>();

        // Jauges D9 : seulement les catégories qui ont un budget, pour ne pas signaler chaque dépense non budgétée.
        var (categories, _, _, budget, _, _) = await BudgetEndpoints.ComputeAsync(db, settings, period, schedule, clock, ct);
        foreach (var c in categories.OrderBy(c => c.SortOrder).ThenBy(c => c.Name))
        {
            var r = budget.Categories[c.Id];
            if (r.Budgeted <= 0 || r.Status is not (BudgetStatus.Warning or BudgetStatus.Exceeded))
            {
                continue;
            }
            var exceeded = r.Status == BudgetStatus.Exceeded;
            var percent = r.Actual / r.Budgeted * 100m;
            alerts.Add(new Alert(
                exceeded ? AlertKind.BudgetExceeded : AlertKind.BudgetWarning,
                new PushMessage(
                    exceeded ? "Budget dépassé" : "Budget bientôt atteint",
                    $"{c.Name} : {NotificationText.Percent(percent)} du budget ({NotificationText.Money(r.Actual)} sur {NotificationText.Money(r.Budgeted)}).",
                    $"/budget/{period.Key}",
                    $"budget-{c.Id}"),
                [(c.Id.ToString(), period.Key)]));
        }

        // F3 : passage prévu sous le seuil, une fois par période.
        var f = await forecast.ComputeAsync(ct);
        if (f.Result.ThresholdDate is { } below)
        {
            alerts.Add(new Alert(
                AlertKind.ForecastBelowThreshold,
                new PushMessage(
                    "Solde prévu sous le seuil",
                    below <= today
                        ? $"Le solde des comptes de la prévision est sous {NotificationText.Money(f.Result.Threshold)}."
                        : $"Passage sous {NotificationText.Money(f.Result.Threshold)} prévu le {NotificationText.DayMonth(below)}.",
                    "/",
                    "forecast"),
                [("forecast", period.Key)]));
        }

        var tomorrow = today.AddDays(1);
        var due = await DueOnAsync(tomorrow, ct);
        if (due.Count > 0)
        {
            alerts.Add(new Alert(
                AlertKind.DueTomorrow,
                new PushMessage(
                    due.Count == 1 ? "Échéance demain" : $"{due.Count} échéances demain",
                    due.Count == 1
                        ? $"{due[0].Label} : {NotificationText.Money(due[0].Amount)}."
                        : NotificationText.List([.. due.Select(d => d.Label)]) + ".",
                    $"/calendrier/{calculator.For(tomorrow).Key}",
                    "due-tomorrow"),
                [.. due.Select(d => (d.RecurrenceId.ToString(), tomorrow.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)))]));
        }

        var pending = await db.RecurrenceOccurrences.AsNoTracking()
            .Where(o => o.Status == OccurrenceStatus.Pending && o.DueDate <= today)
            .Select(o => o.Id)
            .ToListAsync(ct);
        if (pending.Count > 0)
        {
            alerts.Add(new Alert(
                AlertKind.PendingValidation,
                new PushMessage(
                    "Opérations à valider",
                    pending.Count == 1 ? "1 échéance attend votre confirmation." : $"{pending.Count} échéances attendent votre confirmation.",
                    "/a-valider",
                    "pending"),
                [.. pending.Select(id => (id.ToString(), "pending"))]));
        }
        return alerts;
    }

    public sealed record DueEntry(Guid RecurrenceId, string Label, decimal Amount, DateOnly Date);

    /// <summary>Échéances d'une date : à venir (non traitées) ou à valider reportées à cette date, comme le calendrier (E8).</summary>
    public async Task<List<DueEntry>> DueBetweenAsync(DateOnly from, DateOnly to, CancellationToken ct)
    {
        var recurrences = await db.Recurrences.AsNoTracking().Include(r => r.Occurrences)
            .Where(r => r.StartDate <= to.AddDays(BusinessDays.MaxShiftDays))
            .ToListAsync(ct);
        var result = new List<DueEntry>();
        foreach (var r in recurrences)
        {
            var known = r.Occurrences.Select(o => o.ScheduledDate).ToHashSet();
            result.AddRange(r.Occurrences
                .Where(o => o.Status == OccurrenceStatus.Pending && o.DueDate >= from && o.DueDate <= to)
                .Select(o => new DueEntry(r.Id, RecurrenceSchedule.Label(r.Label, r.IsInstallment, o.Index, r.Count), r.Amount, o.DueDate)));
            result.AddRange(r.Rule.Between(from, to)
                .Where(s => !known.Contains(s.Date) && s.Date >= r.GenerateFrom)
                .Select(s => new DueEntry(r.Id, RecurrenceSchedule.Label(r.Label, r.IsInstallment, s.Index, r.Count), r.Amount, s.Date)));
        }
        return [.. result.OrderBy(e => e.Date).ThenBy(e => e.Label, StringComparer.CurrentCulture)];
    }

    private Task<List<DueEntry>> DueOnAsync(DateOnly date, CancellationToken ct) => DueBetweenAsync(date, date, ct);

    private async Task<int> NotifyAsync(AppUser user, NotificationPreference preference, List<PushSubscription> devices, CancellationToken ct)
    {
        var alerts = (await ComputeAsync(ct)).Where(a => preference.Allows(a.Kind)).ToList();
        if (alerts.Count == 0)
        {
            return 0;
        }
        var kinds = alerts.Select(a => a.Kind).Distinct().ToList();
        var sent = (await db.SentAlerts.AsNoTracking()
                .Where(s => s.UserId == user.Id && kinds.Contains(s.Kind))
                .Select(s => new { s.Kind, s.ObjectKey, s.PeriodKey })
                .ToListAsync(ct))
            .Select(s => (s.Kind, s.ObjectKey, s.PeriodKey))
            .ToHashSet();

        var count = 0;
        var now = clock.GetUtcNow();
        foreach (var alert in alerts)
        {
            var fresh = alert.Keys.Where(k => !sent.Contains((alert.Kind, k.ObjectKey, k.PeriodKey))).ToList();
            if (fresh.Count == 0)
            {
                continue;
            }
            // Enregistrée avant l'envoi : un service push en panne ne provoque pas de rafale au retour.
            db.SentAlerts.AddRange(fresh.Select(k => new SentAlert
            {
                UserId = user.Id,
                Kind = alert.Kind,
                ObjectKey = k.ObjectKey,
                PeriodKey = k.PeriodKey,
                SentAt = now,
            }));
            await db.SaveChangesAsync(ct);
            if (await SendToDevicesAsync(devices, alert.Message, ct) > 0)
            {
                count++;
            }
        }
        return count;
    }

    /// <summary>Envoie à chaque appareil ; un abonnement expiré est supprimé.</summary>
    /// <returns>Nombre d'appareils atteints.</returns>
    public async Task<int> SendToDevicesAsync(IReadOnlyList<PushSubscription> devices, PushMessage message, CancellationToken ct)
    {
        var reached = 0;
        foreach (var device in devices)
        {
            switch (await push.SendAsync(device, message, ct))
            {
                case PushOutcome.Sent:
                    reached++;
                    await db.PushSubscriptions.Where(s => s.Id == device.Id)
                        .ExecuteUpdateAsync(s => s.SetProperty(x => x.LastSuccessAt, clock.GetUtcNow()), ct);
                    break;
                case PushOutcome.Gone:
                    await db.PushSubscriptions.Where(s => s.Id == device.Id).ExecuteDeleteAsync(ct);
                    break;
                default:
                    break;
            }
        }
        return reached;
    }

    /// <summary>Période couverte par un résumé : la semaine (lundi à dimanche) ou le mois qui vient de s'achever.</summary>
    public sealed record SummaryWindow(string Key, DateOnly From, DateOnly To, string Label);

    /// <summary>Le résumé part le lundi (hebdomadaire) ou le 1er (mensuel), une seule fois par période couverte.</summary>
    public static SummaryWindow? Window(EmailSummaryFrequency frequency, DateOnly today) => frequency switch
    {
        EmailSummaryFrequency.Weekly when today.DayOfWeek == DayOfWeek.Monday => new SummaryWindow(
            $"{System.Globalization.ISOWeek.GetYear(today.AddDays(-7).ToDateTime(TimeOnly.MinValue))}-W"
            + $"{System.Globalization.ISOWeek.GetWeekOfYear(today.AddDays(-7).ToDateTime(TimeOnly.MinValue)):D2}",
            today.AddDays(-7), today.AddDays(-1),
            $"semaine du {NotificationText.DayMonth(today.AddDays(-7))} au {NotificationText.DayMonth(today.AddDays(-1))}"),
        EmailSummaryFrequency.Monthly when today.Day == 1 => new SummaryWindow(
            today.AddMonths(-1).ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture),
            today.AddMonths(-1), today.AddDays(-1),
            MonthName(today.AddMonths(-1))),
        _ => null,
    };

    private static readonly string[] Months =
        ["janvier", "février", "mars", "avril", "mai", "juin", "juillet", "août", "septembre", "octobre", "novembre", "décembre"];

    /// <summary>« novembre 2026 », sans dépendre des données de culture de l'image.</summary>
    private static string MonthName(DateOnly date) => $"{Months[date.Month - 1]} {date.Year}";

    private async Task<SummaryWindow?> SummaryDueAsync(AppUser user, NotificationPreference preference, DateTimeOffset now, CancellationToken ct)
    {
        if (preference.EmailSummary == EmailSummaryFrequency.None || string.IsNullOrWhiteSpace(user.Email) || !email.IsConfigured)
        {
            return null;
        }
        var window = Window(preference.EmailSummary, DateOnly.FromDateTime(now.DateTime));
        if (window is null)
        {
            return null;
        }
        var already = await db.SentAlerts.AnyAsync(
            s => s.UserId == user.Id && s.Kind == AlertKind.EmailSummary && s.ObjectKey == preference.EmailSummary.ToString() && s.PeriodKey == window.Key, ct);
        return already ? null : window;
    }

    private async Task<bool> SendSummaryAsync(AppUser user, NotificationPreference preference, SummaryWindow window, CancellationToken ct)
    {
        var message = SummaryEmail.Build(user, await BuildSummaryAsync(window, ct), options.Value.PublicUrl);
        try
        {
            await email.SendAsync(message, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Pas d'enregistrement : nouvelle tentative à la vérification suivante.
            LogEmailFailed(user.Id, ex);
            return false;
        }
        db.SentAlerts.Add(new SentAlert
        {
            UserId = user.Id,
            Kind = AlertKind.EmailSummary,
            ObjectKey = preference.EmailSummary.ToString(),
            PeriodKey = window.Key,
            SentAt = clock.GetUtcNow(),
        });
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>Contenu du résumé, calculé avec les comptes visibles de l'utilisateur.</summary>
    public async Task<SummaryData> BuildSummaryAsync(SummaryWindow window, CancellationToken ct)
    {
        var categories = await db.Categories.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Kind, ct);
        var (sums, _) = await BudgetEndpoints.ActualSumsAsync(db, window.From, window.To, ct);
        decimal Sum(params Domain.Categories.CategoryKind[] kinds) =>
            sums.Where(s => categories.TryGetValue(s.Key, out var k) && kinds.Contains(k)).Sum(s => s.Value);

        var computed = await ComputeAsync(ct);
        var f = await forecast.ComputeAsync(ct);
        var today = Balances.Today(clock);
        return new SummaryData(
            window,
            Sum(Domain.Categories.CategoryKind.Income),
            -Sum(Domain.Categories.CategoryKind.Fixed, Domain.Categories.CategoryKind.Variable),
            -Sum(Domain.Categories.CategoryKind.Savings),
            f.Result.CurrentBalance,
            f.Result.EndBalance,
            f.Period.End,
            f.Result.RemainingToLive.Total,
            f.Result.ThresholdDate,
            [.. computed.Where(a => a.Kind is AlertKind.BudgetExceeded or AlertKind.BudgetWarning).Select(a => a.Message.Body)],
            computed.FirstOrDefault(a => a.Kind == AlertKind.PendingValidation)?.Keys.Count ?? 0,
            await DueBetweenAsync(today, today.AddDays(6), ct));
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Échec des notifications de l'utilisateur {UserId}.")]
    private partial void LogUserFailed(Guid userId, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Échec de l'envoi du résumé par e-mail à l'utilisateur {UserId}.")]
    private partial void LogEmailFailed(Guid userId, Exception exception);
}
