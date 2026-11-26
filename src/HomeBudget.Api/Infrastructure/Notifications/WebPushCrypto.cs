using System.Buffers.Binary;
using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace HomeBudget.Api.Infrastructure.Notifications;

/// <summary>
/// Chiffrement d'un message Web Push (RFC 8291, encodage « aes128gcm » de la RFC 8188), avec les seules
/// primitives de .NET : ECDH P-256, HKDF-SHA256 et AES-128-GCM. Le service push ne voit jamais le contenu.
/// </summary>
public static class WebPushCrypto
{
    public const int RecordSize = 4096;

    /// <summary>Taille maximale du contenu en clair : un seul enregistrement de 4 096 octets.</summary>
    public const int MaxPayloadLength = RecordSize - HeaderOverhead - TagLength - 1;

    private const int KeyLength = 65;
    private const int SaltLength = 16;
    private const int TagLength = 16;
    private const int HeaderOverhead = SaltLength + 4 + 1 + KeyLength;

    /// <param name="userAgentPublicKey">Clé publique de l'appareil (« p256dh », point P-256 non compressé de 65 octets).</param>
    /// <param name="authSecret">Secret d'authentification de l'appareil (« auth », 16 octets).</param>
    /// <param name="senderKey">Clé éphémère du serveur ; une nouvelle à chaque message si null (fixée par les tests).</param>
    /// <param name="salt">Sel de 16 octets ; aléatoire si null (fixé par les tests).</param>
    /// <returns>Le corps de la requête : en-tête aes128gcm (sel, taille d'enregistrement, clé du serveur) puis le chiffré.</returns>
    public static byte[] Encrypt(
        ReadOnlySpan<byte> payload, byte[] userAgentPublicKey, byte[] authSecret, ECDiffieHellman? senderKey = null, byte[]? salt = null)
    {
        if (payload.Length > MaxPayloadLength)
        {
            throw new ArgumentException($"Message push trop long ({payload.Length} octets, {MaxPayloadLength} au plus).", nameof(payload));
        }
        if (userAgentPublicKey is not [0x04, ..] || userAgentPublicKey.Length != KeyLength)
        {
            throw new ArgumentException("Clé publique de l'appareil invalide.", nameof(userAgentPublicKey));
        }
        if (authSecret.Length != 16)
        {
            throw new ArgumentException("Secret d'authentification invalide.", nameof(authSecret));
        }

        using var ownedKey = senderKey is null ? ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256) : null;
        var serverKey = senderKey ?? ownedKey!;
        var serverPublic = ExportPublicKey(serverKey);
        salt ??= RandomNumberGenerator.GetBytes(SaltLength);

        using var peer = ImportPublicKey(userAgentPublicKey);
        var sharedSecret = serverKey.DeriveRawSecretAgreement(peer.PublicKey);

        // RFC 8291 §3.4 : IKM = HKDF(auth_secret, ecdh_secret, "WebPush: info" || 0x00 || ua_public || as_public, 32).
        var keyInfo = Concat(Encoding.ASCII.GetBytes("WebPush: info\0"), userAgentPublicKey, serverPublic);
        var prkKey = HKDF.Extract(HashAlgorithmName.SHA256, sharedSecret, authSecret);
        var ikm = HKDF.Expand(HashAlgorithmName.SHA256, prkKey, 32, keyInfo);

        // RFC 8188 §2.2 et §2.3 : clé de contenu et nonce dérivés du sel.
        var prk = HKDF.Extract(HashAlgorithmName.SHA256, ikm, salt);
        var cek = HKDF.Expand(HashAlgorithmName.SHA256, prk, 16, Encoding.ASCII.GetBytes("Content-Encoding: aes128gcm\0"));
        var nonce = HKDF.Expand(HashAlgorithmName.SHA256, prk, 12, Encoding.ASCII.GetBytes("Content-Encoding: nonce\0"));

        // Un seul enregistrement, donc le dernier : délimiteur 0x02, sans remplissage.
        var plaintext = new byte[payload.Length + 1];
        payload.CopyTo(plaintext);
        plaintext[^1] = 0x02;

        var body = new byte[HeaderOverhead + plaintext.Length + TagLength];
        salt.CopyTo(body, 0);
        BinaryPrimitives.WriteUInt32BigEndian(body.AsSpan(SaltLength, 4), RecordSize);
        body[SaltLength + 4] = KeyLength;
        serverPublic.CopyTo(body, SaltLength + 5);
        using var aes = new AesGcm(cek, TagLength);
        aes.Encrypt(nonce, plaintext, body.AsSpan(HeaderOverhead, plaintext.Length), body.AsSpan(HeaderOverhead + plaintext.Length, TagLength));
        return body;
    }

    /// <summary>Point P-256 non compressé (0x04 || X || Y).</summary>
    public static byte[] ExportPublicKey(ECAlgorithm key)
    {
        var p = key.ExportParameters(false);
        return Concat([0x04], p.Q.X!, p.Q.Y!);
    }

    public static ECDiffieHellman ImportPublicKey(byte[] point) => ECDiffieHellman.Create(new ECParameters
    {
        Curve = ECCurve.NamedCurves.nistP256,
        Q = new ECPoint { X = point[1..33], Y = point[33..65] },
    });

    /// <summary>Décode une clé base64url ; null si elle est mal formée.</summary>
    public static byte[]? DecodeKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        try
        {
            return Base64Url.DecodeFromChars(value.Trim().TrimEnd('='));
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static byte[] Concat(params byte[][] parts)
    {
        var result = new byte[parts.Sum(p => p.Length)];
        var offset = 0;
        foreach (var part in parts)
        {
            part.CopyTo(result, offset);
            offset += part.Length;
        }
        return result;
    }
}
