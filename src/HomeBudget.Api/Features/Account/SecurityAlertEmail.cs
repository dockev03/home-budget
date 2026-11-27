using System.Globalization;
using System.Net;
using System.Text;

using HomeBudget.Api.Domain.Users;
using HomeBudget.Api.Infrastructure.Notifications;
using HomeBudget.Api.Infrastructure.Security;

namespace HomeBudget.Api.Features.Account;

/// <summary>E-mails d'alerte de sécurité (N9) : nouvelle connexion, compte verrouillé. Texte brut et HTML sobre.</summary>
public static class SecurityAlertEmail
{
    public static EmailMessage Build(AppUser user, SecurityAlert alert, string? publicUrl, TimeZoneInfo zone)
    {
        var device = UserAgentInfo.Parse(alert.UserAgent);
        var rows = new List<(string Label, string Value)>
        {
            ("Date", DateTime(alert.At, zone)),
            ("Adresse IP", alert.IpAddress ?? "inconnue"),
            ("Navigateur", device.Browser),
            ("Système", device.Device switch
            {
                DeviceKind.Desktop => $"{device.OperatingSystem} (ordinateur)",
                DeviceKind.Mobile => $"{device.OperatingSystem} (mobile)",
                DeviceKind.Tablet => $"{device.OperatingSystem} (tablette)",
                _ => device.OperatingSystem,
            }),
        };
        string subject;
        string intro;
        string[] advice;
        if (alert.Kind == SecurityAlertKind.Login)
        {
            subject = "Home Budget · nouvelle connexion à votre compte";
            intro = "Une connexion à votre compte Home Budget vient d'avoir lieu :";
            rows.Add(("Méthode", Method(alert.Origin)));
            advice =
            [
                "Si c'est vous, il n'y a rien à faire.",
                "Sinon, ouvrez Sécurité, révoquez cette session et changez votre mot de passe ; activez la double authentification si ce n'est pas déjà fait.",
            ];
        }
        else
        {
            subject = "Home Budget · compte verrouillé après plusieurs échecs de connexion";
            var until = alert.LockedUntil is { } end ? $" jusqu'à {Time(end, zone)}" : string.Empty;
            intro = $"Votre compte Home Budget est verrouillé{until} après plusieurs tentatives de connexion échouées. Dernière tentative :";
            advice =
            [
                "Si c'est vous, patientez avant de réessayer.",
                "Sinon, quelqu'un essaie peut-être de deviner votre mot de passe : choisissez une phrase de passe longue et unique, et activez la double authentification.",
            ];
        }
        var link = string.IsNullOrWhiteSpace(publicUrl) ? null : publicUrl.TrimEnd('/') + "/securite";
        const string Footer = "Pour ne plus recevoir ces alertes : menu du compte › Sécurité › Alertes par e-mail.";

        var text = new StringBuilder();
        text.AppendLine(CultureInfo.InvariantCulture, $"Bonjour {user.Name},").AppendLine().AppendLine(intro).AppendLine();
        rows.ForEach(r => text.AppendLine(CultureInfo.InvariantCulture, $"- {r.Label} : {r.Value}"));
        text.AppendLine();
        foreach (var line in advice)
        {
            text.AppendLine(line);
        }
        if (link is not null)
        {
            text.AppendLine().AppendLine(link);
        }
        text.AppendLine().AppendLine(Footer);

        static string E(string value) => WebUtility.HtmlEncode(value);
        var html = new StringBuilder();
        html.Append("<!doctype html><html lang=\"fr\"><body style=\"font-family:sans-serif;color:#1f2937\">");
        html.Append(CultureInfo.InvariantCulture, $"<p>Bonjour {E(user.Name)},</p><p>{E(intro)}</p>");
        html.Append("<table style=\"border-collapse:collapse\">");
        foreach (var (label, value) in rows)
        {
            html.Append(CultureInfo.InvariantCulture,
                $"<tr><td style=\"padding:2px 16px 2px 0\">{E(label)}</td><td style=\"padding:2px 0\"><strong>{E(value)}</strong></td></tr>");
        }
        html.Append("</table>");
        foreach (var line in advice)
        {
            html.Append(CultureInfo.InvariantCulture, $"<p>{E(line)}</p>");
        }
        if (link is not null)
        {
            html.Append(CultureInfo.InvariantCulture, $"<p><a href=\"{E(link)}\">Ouvrir Sécurité</a></p>");
        }
        html.Append(CultureInfo.InvariantCulture, $"<p style=\"color:#6b7280;font-size:12px\">{E(Footer)}</p></body></html>");

        return new EmailMessage(user.Email!, subject, text.ToString(), html.ToString());
    }

    public static string Method(SessionOrigin? origin) => origin switch
    {
        SessionOrigin.Authenticator => "mot de passe et code TOTP",
        SessionOrigin.RecoveryCode => "mot de passe et code de secours",
        SessionOrigin.Legacy => "session antérieure",
        _ => "mot de passe",
    };

    private static string DateTime(DateTimeOffset at, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTime(at, zone).ToString("dd/MM/yyyy 'à' HH:mm", CultureInfo.InvariantCulture);

    private static string Time(DateTimeOffset at, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTime(at, zone).ToString("HH:mm", CultureInfo.InvariantCulture);
}
