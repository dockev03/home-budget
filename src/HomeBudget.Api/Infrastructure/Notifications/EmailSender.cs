using HomeBudget.Api.Infrastructure.Configuration;

using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;

using Microsoft.Extensions.Options;

using MimeKit;

namespace HomeBudget.Api.Infrastructure.Notifications;

/// <summary>Message à envoyer : texte brut et HTML (sans ressource externe).</summary>
public sealed record EmailMessage(string To, string Subject, string Text, string Html);

/// <summary>Envoi d'un e-mail, remplaçable dans les tests.</summary>
public interface IEmailSender
{
    bool IsConfigured { get; }

    Task SendAsync(EmailMessage message, CancellationToken ct);
}

/// <summary>Envoi SMTP générique (M2, Q2) : Gmail avec un mot de passe d'application, ou un serveur tiers.</summary>
public sealed class SmtpEmailSender(IOptions<HomeBudgetOptions> options) : IEmailSender
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    public bool IsConfigured => options.Value.Smtp.IsConfigured;

    public async Task SendAsync(EmailMessage message, CancellationToken ct)
    {
        var smtp = options.Value.Smtp;
        if (!smtp.IsConfigured)
        {
            throw new InvalidOperationException("Envoi d'e-mails non configuré (HB__Smtp__Host et HB__Smtp__From).");
        }

        var mime = new MimeMessage();
        mime.From.Add(MailboxAddress.Parse(smtp.From!));
        mime.To.Add(MailboxAddress.Parse(message.To));
        mime.Subject = message.Subject;
        mime.Body = new BodyBuilder { TextBody = message.Text, HtmlBody = message.Html }.ToMessageBody();

        using var client = new SmtpClient { Timeout = (int)Timeout.TotalMilliseconds };
        await client.ConnectAsync(smtp.Host!, smtp.Port, SecureSocketOptions(smtp), ct);
        if (!string.IsNullOrEmpty(smtp.User))
        {
            await client.AuthenticateAsync(smtp.User, smtp.Password ?? string.Empty, ct);
        }
        await client.SendAsync(new SendRequest(mime), ct);
        await client.DisconnectAsync(true, ct);
    }

    internal static SecureSocketOptions SecureSocketOptions(SmtpOptions smtp) => smtp.Security switch
    {
        SmtpSecurity.SslOnConnect => MailKit.Security.SecureSocketOptions.SslOnConnect,
        SmtpSecurity.StartTls => MailKit.Security.SecureSocketOptions.StartTls,
        SmtpSecurity.None => MailKit.Security.SecureSocketOptions.None,
        // Auto : SSL implicite sur 465, STARTTLS exigé ailleurs (jamais d'envoi en clair par défaut).
        _ => smtp.Port == 465 ? MailKit.Security.SecureSocketOptions.SslOnConnect : MailKit.Security.SecureSocketOptions.StartTls,
    };
}
