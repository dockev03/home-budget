using System.Globalization;
using System.Security.Cryptography;
using System.Text;

using Microsoft.AspNetCore.DataProtection;

namespace HomeBudget.Api.Infrastructure.Webhooks;

/// <summary>
/// Signature des webhooks (I5) : <c>X-HomeBudget-Signature: sha256=&lt;hex&gt;</c>, HMAC-SHA256 calculé avec le secret
/// (ses octets UTF-8) sur <c>"{X-HomeBudget-Timestamp}.{corps}"</c>. Le destinataire refait le calcul, compare en
/// temps constant et rejette un horodatage trop ancien (rejeu).
/// </summary>
public static class WebhookSigner
{
    public const string SignatureHeader = "X-HomeBudget-Signature";
    public const string TimestampHeader = "X-HomeBudget-Timestamp";
    public const string EventHeader = "X-HomeBudget-Event";
    public const string DeliveryHeader = "X-HomeBudget-Delivery";

    public static string Sign(string secret, long timestamp, string body)
    {
        var data = Encoding.UTF8.GetBytes(timestamp.ToString(CultureInfo.InvariantCulture) + "." + body);
        return "sha256=" + Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), data));
    }

    /// <summary>Nouveau secret : « whsec_ » suivi de 32 octets aléatoires en base64url.</summary>
    public static string GenerateSecret() =>
        "whsec_" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

/// <summary>Chiffrement du secret des webhooks en base, par la protection des données ASP.NET.</summary>
public sealed class WebhookSecrets(IDataProtectionProvider provider)
{
    private readonly IDataProtector protector = provider.CreateProtector("HomeBudget.Webhooks.Secret");

    public string Protect(string secret) => protector.Protect(secret);

    public string Unprotect(string protectedSecret) => protector.Unprotect(protectedSecret);
}
