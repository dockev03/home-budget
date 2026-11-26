using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using HomeBudget.Api.Infrastructure.Notifications;

namespace HomeBudget.Tests.Notifications;

public sealed class WebPushCryptoTests
{
    private static byte[] B64(string value) => Base64Url.DecodeFromChars(value);

    [Fact]
    public void Reproduit_l_exemple_de_la_RFC_8291()
    {
        // RFC 8291, annexe A.
        var plaintext = Encoding.UTF8.GetBytes("When I grow up, I want to be a watermelon");
        var auth = B64("BTBZMqHH6r4Tts7J_aSIgg");
        var receiverPublic = B64("BCVxsr7N_eNgVRqvHtD0zTZsEc6-VV-JvLexhqUzORcxaOzi6-AYWXvTBHm4bjyPjs7Vd8pZGH6SRpkNtoIAiw4");
        var senderPublic = B64("BP4z9KsN6nGRTbVYI_c7VJSPQTBtkgcy27mlmlMoZIIgDll6e3vCYLocInmYWAmS6TlzAC8wEqKK6PBru3jl7A8");
        var senderPrivate = B64("yfWPiYE-n46HLnH0KqZOF1fJJU3MYrct3AELtAQ-oRw");
        var salt = B64("DGv6ra1nlYgDCS1FRnbzlw");
        using var sender = ECDiffieHellman.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            D = senderPrivate,
            Q = new ECPoint { X = senderPublic[1..33], Y = senderPublic[33..65] },
        });

        var body = WebPushCrypto.Encrypt(plaintext, receiverPublic, auth, sender, salt);

        Base64Url.EncodeToString(body).ShouldBe(
            "DGv6ra1nlYgDCS1FRnbzlwAAEABBBP4z9KsN6nGRTbVYI_c7VJSPQTBtkgcy27mlmlMoZIIgDll6e3vCYLocInmYWAmS6TlzAC8wEqKK6PBru3jl7A_yl95bQpu6cVPTpK4Mqgkf1CXztLVBSt2Ks3oZwbuwXPXLWyouBWLVWGNWQexSgSxsj_Qulcy4a-fN");
        using var receiver = new WebPushDecryptor(B64("q1dXpw3UpT5VOmu_cf_v6ih07Aems3njxI-JWgLcM94"), receiverPublic, auth);
        receiver.Decrypt(body).ShouldBe(plaintext);
    }

    [Fact]
    public void Un_appareil_dechiffre_le_message()
    {
        using var device = new WebPushDecryptor();
        var message = JsonSerializer.SerializeToUtf8Bytes(new { title = "Budget dépassé", body = "Courses : 105 %" });

        var first = WebPushCrypto.Encrypt(message, device.PublicKey, device.AuthSecret);
        var second = WebPushCrypto.Encrypt(message, device.PublicKey, device.AuthSecret);

        device.Decrypt(first).ShouldBe(message);
        // Clé éphémère et sel aléatoires : deux envois du même message diffèrent.
        first.ShouldNotBe(second);
    }

    [Fact]
    public void Refuse_un_message_trop_long_ou_une_cle_invalide()
    {
        using var device = new WebPushDecryptor();
        Should.Throw<ArgumentException>(() =>
            WebPushCrypto.Encrypt(new byte[WebPushCrypto.MaxPayloadLength + 1], device.PublicKey, device.AuthSecret));
        Should.Throw<ArgumentException>(() => WebPushCrypto.Encrypt("x"u8, device.PublicKey[..64], device.AuthSecret));
        Should.Throw<ArgumentException>(() => WebPushCrypto.Encrypt("x"u8, device.PublicKey, new byte[8]));
        WebPushCrypto.Encrypt(new byte[WebPushCrypto.MaxPayloadLength], device.PublicKey, device.AuthSecret).Length.ShouldBe(4096);
    }

    [Theory]
    [InlineData("https://fcm.googleapis.com/fcm/send/abc", true)]
    [InlineData("https://updates.push.services.mozilla.com/wpush/v2/abc", true)]
    [InlineData("https://web.push.apple.com/QGx", true)]
    [InlineData("https://wns2-par02p.notify.windows.com/w/?token=x", true)]
    [InlineData("http://fcm.googleapis.com/fcm/send/abc", false)]
    [InlineData("https://fcm.googleapis.com:8443/fcm/send/abc", false)]
    [InlineData("https://evil-fcm.googleapis.com.attacker.test/x", false)]
    [InlineData("https://notfcm.googleapis.com.example/x", false)]
    [InlineData("https://192.168.1.10/push", false)]
    [InlineData("https://user:pass@fcm.googleapis.com/x", false)]
    [InlineData("not a url", false)]
    public void N_accepte_que_les_services_push_connus(string endpoint, bool allowed) =>
        WebPushSender.IsAllowedEndpoint(endpoint).ShouldBe(allowed);
}

public sealed class VapidTests
{
    [Fact]
    public void Signe_un_jeton_ES256_verifiable_avec_la_cle_publique()
    {
        var (publicKey, privateKey) = Vapid.Generate();
        using var vapid = Vapid.Load(publicKey, privateKey, "mailto:admin@example.test");
        var now = new DateTimeOffset(2026, 11, 26, 10, 0, 0, TimeSpan.Zero);

        var header = vapid.Authorization(new Uri("https://fcm.googleapis.com/fcm/send/abc?x=1"), now);

        header.ShouldStartWith("vapid t=");
        header.ShouldEndWith($", k={publicKey}");
        var token = header["vapid t=".Length..header.IndexOf(',', StringComparison.Ordinal)];
        var parts = token.Split('.');
        parts.Length.ShouldBe(3);
        using var claims = JsonDocument.Parse(Base64Url.DecodeFromChars(parts[1]));
        claims.RootElement.GetProperty("aud").GetString().ShouldBe("https://fcm.googleapis.com");
        claims.RootElement.GetProperty("sub").GetString().ShouldBe("mailto:admin@example.test");
        claims.RootElement.GetProperty("exp").GetInt64().ShouldBe((now + Vapid.TokenLifetime).ToUnixTimeSeconds());

        var q = Base64Url.DecodeFromChars(publicKey);
        using var verifier = ECDsa.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint { X = q[1..33], Y = q[33..65] },
        });
        var signature = Base64Url.DecodeFromChars(parts[2]);
        signature.Length.ShouldBe(64);
        verifier.VerifyData(Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}"), signature, HashAlgorithmName.SHA256).ShouldBeTrue();
    }

    [Fact]
    public void Refuse_des_cles_invalides_ou_qui_ne_vont_pas_ensemble()
    {
        var (publicKey, _) = Vapid.Generate();
        var (_, otherPrivate) = Vapid.Generate();
        Should.Throw<InvalidOperationException>(() => Vapid.Load("abc", "def", "mailto:a@b.c"));
        Should.Throw<InvalidOperationException>(() => Vapid.Load(publicKey, otherPrivate, "mailto:a@b.c"));
    }
}
