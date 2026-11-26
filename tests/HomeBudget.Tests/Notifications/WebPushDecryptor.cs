using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

using HomeBudget.Api.Infrastructure.Notifications;

namespace HomeBudget.Tests.Notifications;

/// <summary>
/// Déchiffrement côté navigateur (RFC 8291), écrit indépendamment du code de production pour vérifier
/// qu'un appareil réel saurait lire les messages envoyés.
/// </summary>
public sealed class WebPushDecryptor : IDisposable
{
    private readonly ECDiffieHellman key = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);

    public WebPushDecryptor()
    {
        PublicKey = WebPushCrypto.ExportPublicKey(key);
        AuthSecret = RandomNumberGenerator.GetBytes(16);
    }

    public WebPushDecryptor(byte[] privateKey, byte[] publicKey, byte[] authSecret)
    {
        key = ECDiffieHellman.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            D = privateKey,
            Q = new ECPoint { X = publicKey[1..33], Y = publicKey[33..65] },
        });
        PublicKey = publicKey;
        AuthSecret = authSecret;
    }

    public byte[] PublicKey { get; }

    public byte[] AuthSecret { get; }

    public string P256dh => System.Buffers.Text.Base64Url.EncodeToString(PublicKey);

    public string Auth => System.Buffers.Text.Base64Url.EncodeToString(AuthSecret);

    public byte[] Decrypt(byte[] body)
    {
        var salt = body[..16];
        var recordSize = BinaryPrimitives.ReadUInt32BigEndian(body.AsSpan(16, 4));
        recordSize.ShouldBe(4096u);
        var idLength = body[20];
        var senderPublic = body[21..(21 + idLength)];
        var ciphertext = body[(21 + idLength)..];

        using var sender = WebPushCrypto.ImportPublicKey(senderPublic);
        var shared = key.DeriveRawSecretAgreement(sender.PublicKey);
        var info = Encoding.ASCII.GetBytes("WebPush: info\0").Concat(PublicKey).Concat(senderPublic).ToArray();
        var ikm = HKDF.DeriveKey(HashAlgorithmName.SHA256, shared, 32, AuthSecret, info);
        var cek = HKDF.DeriveKey(HashAlgorithmName.SHA256, ikm, 16, salt, Encoding.ASCII.GetBytes("Content-Encoding: aes128gcm\0"));
        var nonce = HKDF.DeriveKey(HashAlgorithmName.SHA256, ikm, 12, salt, Encoding.ASCII.GetBytes("Content-Encoding: nonce\0"));

        var plaintext = new byte[ciphertext.Length - 16];
        using var aes = new AesGcm(cek, 16);
        aes.Decrypt(nonce, ciphertext[..^16], ciphertext[^16..], plaintext);
        // Dernier enregistrement : délimiteur 0x02 suivi d'éventuels zéros de remplissage.
        var end = Array.LastIndexOf(plaintext, (byte)0x02);
        end.ShouldBeGreaterThanOrEqualTo(0);
        plaintext[(end + 1)..].ShouldAllBe(b => b == 0);
        return plaintext[..end];
    }

    public void Dispose() => key.Dispose();
}
