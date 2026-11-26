using System.Net;
using System.Text;

using HomeBudget.Api.Domain.Users;
using HomeBudget.Api.Infrastructure.Notifications;

namespace HomeBudget.Api.Features.Notifications;

/// <param name="Income">Revenus de la période couverte.</param>
/// <param name="Expenses">Dépenses fixes et variables de la période couverte, en positif.</param>
/// <param name="Savings">Épargne de la période couverte, en positif.</param>
/// <param name="ForecastDate">Fin de la période budgétaire en cours.</param>
/// <param name="Budgets">Budgets en alerte de la période en cours, déjà rédigés.</param>
/// <param name="Upcoming">Échéances des 7 prochains jours.</param>
public sealed record SummaryData(
    AlertService.SummaryWindow Window,
    decimal Income,
    decimal Expenses,
    decimal Savings,
    decimal CurrentBalance,
    decimal ForecastBalance,
    DateOnly ForecastDate,
    decimal RemainingToLive,
    DateOnly? ThresholdDate,
    IReadOnlyList<string> Budgets,
    int Pending,
    IReadOnlyList<AlertService.DueEntry> Upcoming);

/// <summary>Résumé par e-mail (M2) : texte brut et HTML sobre, sans image ni ressource externe.</summary>
public static class SummaryEmail
{
    private const int MaxUpcoming = 10;

    public static EmailMessage Build(AppUser user, SummaryData data, string? publicUrl)
    {
        var lines = new List<(string Label, string Value)>
        {
            ("Revenus", NotificationText.Money(data.Income)),
            ("Dépenses", NotificationText.Money(data.Expenses)),
            ("Épargne", NotificationText.Money(data.Savings)),
        };
        var position = new List<(string Label, string Value)>
        {
            ("Solde actuel", NotificationText.Money(data.CurrentBalance)),
            ($"Solde prévu au {NotificationText.Date(data.ForecastDate)}", NotificationText.Money(data.ForecastBalance)),
            ("Reste à vivre", NotificationText.Money(data.RemainingToLive)),
        };
        var notes = new List<string>();
        if (data.ThresholdDate is { } below)
        {
            notes.Add($"Passage sous le seuil prévu le {NotificationText.Date(below)}.");
        }
        notes.AddRange(data.Budgets);
        if (data.Pending > 0)
        {
            notes.Add(data.Pending == 1 ? "1 échéance à valider." : $"{data.Pending} échéances à valider.");
        }
        var upcoming = data.Upcoming.Take(MaxUpcoming)
            .Select(d => $"{NotificationText.DayMonth(d.Date)} · {d.Label} · {NotificationText.Money(d.Amount)}")
            .ToList();
        if (data.Upcoming.Count > MaxUpcoming)
        {
            upcoming.Add($"… et {data.Upcoming.Count - MaxUpcoming} autres.");
        }
        var link = string.IsNullOrWhiteSpace(publicUrl) ? null : publicUrl.TrimEnd('/') + "/";
        var subject = $"Home Budget · résumé de la {data.Window.Label}";
        if (!data.Window.Label.StartsWith("semaine", StringComparison.Ordinal))
        {
            subject = $"Home Budget · résumé de {data.Window.Label}";
        }

        var text = new StringBuilder();
        text.AppendLine($"Bonjour {user.Name},").AppendLine();
        text.AppendLine($"Résumé de la période : {data.Window.Label}.").AppendLine();
        foreach (var (label, value) in lines)
        {
            text.AppendLine($"{label} : {value}");
        }
        text.AppendLine().AppendLine("Période en cours :");
        foreach (var (label, value) in position)
        {
            text.AppendLine($"{label} : {value}");
        }
        if (notes.Count > 0)
        {
            text.AppendLine().AppendLine("À surveiller :");
            notes.ForEach(n => text.AppendLine($"- {n}"));
        }
        if (upcoming.Count > 0)
        {
            text.AppendLine().AppendLine("Échéances des 7 prochains jours :");
            upcoming.ForEach(u => text.AppendLine($"- {u}"));
        }
        if (link is not null)
        {
            text.AppendLine().AppendLine(link);
        }
        text.AppendLine().AppendLine("Pour ne plus recevoir ce résumé : menu du compte › Notifications.");

        static string E(string value) => WebUtility.HtmlEncode(value);
        static string Table(IEnumerable<(string Label, string Value)> rows) =>
            "<table style=\"border-collapse:collapse\">" + string.Concat(rows.Select(r =>
                $"<tr><td style=\"padding:2px 16px 2px 0\">{E(r.Label)}</td><td style=\"padding:2px 0;text-align:right\"><strong>{E(r.Value)}</strong></td></tr>"))
            + "</table>";
        static string Items(IEnumerable<string> items) => "<ul>" + string.Concat(items.Select(i => $"<li>{E(i)}</li>")) + "</ul>";

        var html = new StringBuilder();
        html.Append("<!doctype html><html lang=\"fr\"><body style=\"font-family:sans-serif;color:#1f2937\">");
        html.Append($"<p>Bonjour {E(user.Name)},</p>");
        html.Append($"<h2 style=\"font-size:18px\">Résumé · {E(data.Window.Label)}</h2>");
        html.Append(Table(lines));
        html.Append("<h3 style=\"font-size:16px\">Période en cours</h3>").Append(Table(position));
        if (notes.Count > 0)
        {
            html.Append("<h3 style=\"font-size:16px\">À surveiller</h3>").Append(Items(notes));
        }
        if (upcoming.Count > 0)
        {
            html.Append("<h3 style=\"font-size:16px\">Échéances des 7 prochains jours</h3>").Append(Items(upcoming));
        }
        if (link is not null)
        {
            html.Append($"<p><a href=\"{E(link)}\">Ouvrir Home Budget</a></p>");
        }
        html.Append("<p style=\"color:#6b7280;font-size:12px\">Pour ne plus recevoir ce résumé : menu du compte › Notifications.</p>");
        html.Append("</body></html>");

        return new EmailMessage(user.Email!, subject, text.ToString(), html.ToString());
    }
}
