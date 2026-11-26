using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace HomeBudget.Api.Infrastructure.Notifications;

/// <summary>
/// Identification du serveur auprès des services push (VAPID, RFC 8292) : un jeton JWT signé en ES256 avec la
/// clé privée VAPID, valable 12 heures, pour l'origine de l'adresse push.
/// </summary>
public sealed class Vapid : IDisposable
{
    public static readonly TimeSpan TokenLifetime = TimeSpan.FromHours(12);

    private readonly ECDsa key;

    private Vapid(ECDsa key, string publicKey, string subject)
    {
        this.key = key;
        PublicKey = publicKey;
        Subject = subject;
    }

    /// <summary>Clé publique en base64url : la « applicationServerKey » donnée au navigateur.</summary>
    public string PublicKey { get; }

    public string Subject { get; }

    /// <summary>Charge les clés configurées ; lève une exception explicite si elles sont invalides ou ne vont pas ensemble.</summary>
    public static Vapid Load(string publicKey, string privateKey, string subject)
    {
        var q = WebPushCrypto.DecodeKey(publicKey);
        var d = WebPushCrypto.DecodeKey(privateKey);
        if (q is not [0x04, ..] || q.Length != 65 || d is not { Length: 32 })
        {
            throw new InvalidOperationException("Clés VAPID invalides : attendues en base64url, publique de 65 octets et privée de 32 octets.");
        }
        ECDsa key;
        try
        {
            key = ECDsa.Create(new ECParameters
            {
                Curve = ECCurve.NamedCurves.nistP256,
                D = d,
                Q = new ECPoint { X = q[1..33], Y = q[33..65] },
            });
        }
        catch (CryptographicException ex)
        {
            throw new InvalidOperationException("Clés VAPID invalides : la clé publique ne correspond pas à la clé privée.", ex);
        }
        return new Vapid(key, Base64Url.EncodeToString(q), subject);
    }

    /// <summary>Nouvelle paire de clés, pour <c>--generate-vapid-keys</c>.</summary>
    public static (string PublicKey, string PrivateKey) Generate()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var p = key.ExportParameters(true);
        return (Base64Url.EncodeToString(WebPushCrypto.ExportPublicKey(key)), Base64Url.EncodeToString(p.D));
    }

    /// <summary>Valeur de l'en-tête Authorization pour une adresse push.</summary>
    public string Authorization(Uri endpoint, DateTimeOffset now)
    {
        var audience = endpoint.GetLeftPart(UriPartial.Authority);
        var header = Encode("""{"typ":"JWT","alg":"ES256"}"""u8);
        var claims = Encode(JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object>
        {
            ["aud"] = audience,
            ["exp"] = (now + TokenLifetime).ToUnixTimeSeconds(),
            ["sub"] = Subject,
        }));
        var signingInput = $"{header}.{claims}";
        // Signature ES256 au format JWS : r || s sur 64 octets (format par défaut de .NET).
        var signature = key.SignData(Encoding.ASCII.GetBytes(signingInput), HashAlgorithmName.SHA256);
        return $"vapid t={signingInput}.{Encode(signature)}, k={PublicKey}";
    }

    public void Dispose() => key.Dispose();

    private static string Encode(ReadOnlySpan<byte> bytes) => Base64Url.EncodeToString(bytes);
}
