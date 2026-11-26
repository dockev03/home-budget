using System.Security.Cryptography;
using System.Text;

namespace HomeBudget.Api.Domain.Users;

/// <summary>
/// Invitation d'un membre du foyer (K1) : un lien à usage unique et à durée limitée. Seule l'empreinte
/// SHA-256 du jeton est conservée ; le jeton n'est montré qu'une fois, à l'administrateur qui invite.
/// </summary>
public sealed class Invitation : AuditableEntity
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(48);

    public required string UserName { get; set; }

    public string? DisplayName { get; set; }

    public required string Role { get; set; }

    public required string TokenHash { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset? AcceptedAt { get; set; }

    public Guid? AcceptedUserId { get; set; }

    public bool IsPending(DateTimeOffset now) => AcceptedAt is null && ExpiresAt > now;

    /// <summary>Jeton aléatoire de 256 bits, encodé pour une URL.</summary>
    public static string NewToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token.Trim())));
}
