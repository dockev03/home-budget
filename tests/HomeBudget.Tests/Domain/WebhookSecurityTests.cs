using System.Net;
using System.Security.Cryptography;
using System.Text;

using HomeBudget.Api.Infrastructure.Security;
using HomeBudget.Api.Infrastructure.Webhooks;

namespace HomeBudget.Tests.Domain;

public sealed class WebhookSecurityTests
{
    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("127.10.0.1")]
    [InlineData("0.0.0.0")]
    [InlineData("169.254.169.254")]
    [InlineData("224.0.0.1")]
    [InlineData("255.255.255.255")]
    [InlineData("::1")]
    [InlineData("::")]
    [InlineData("fe80::1")]
    [InlineData("::ffff:127.0.0.1")]
    [InlineData("::ffff:169.254.169.254")]
    public void Loopback_link_local_and_metadata_are_always_denied(string address)
    {
        WebhookAddressPolicy.IsAllowed(IPAddress.Parse(address), allowPrivateNetworks: true).ShouldBeFalse();
        WebhookAddressPolicy.IsAllowed(IPAddress.Parse(address), allowPrivateNetworks: false).ShouldBeFalse();
    }

    [Theory]
    [InlineData("10.0.0.5")]
    [InlineData("172.18.0.3")]
    [InlineData("192.168.1.20")]
    [InlineData("100.100.1.1")]
    [InlineData("fd12:3456::1")]
    [InlineData("::ffff:192.168.1.20")]
    public void Private_networks_follow_the_setting(string address)
    {
        WebhookAddressPolicy.IsAllowed(IPAddress.Parse(address), allowPrivateNetworks: true).ShouldBeTrue();
        WebhookAddressPolicy.IsAllowed(IPAddress.Parse(address), allowPrivateNetworks: false).ShouldBeFalse();
    }

    [Theory]
    [InlineData("93.184.215.14")]
    [InlineData("172.32.0.1")]
    [InlineData("2606:4700::1111")]
    public void Public_addresses_are_allowed(string address) =>
        WebhookAddressPolicy.IsAllowed(IPAddress.Parse(address), allowPrivateNetworks: false).ShouldBeTrue();

    [Fact]
    public void Signature_is_hmac_sha256_of_timestamp_and_body()
    {
        const string secret = "whsec_test";
        const string body = "{\"event\":\"ping\"}";
        var expected = Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes("1700000000." + body)));

        WebhookSigner.Sign(secret, 1_700_000_000, body).ShouldBe("sha256=" + expected);
        WebhookSigner.Sign(secret, 1_700_000_001, body).ShouldNotBe("sha256=" + expected);
    }

    [Fact]
    public void Secrets_and_tokens_are_random_and_prefixed()
    {
        var secret = WebhookSigner.GenerateSecret();
        secret.ShouldStartWith("whsec_");
        secret.Length.ShouldBe(6 + 43);
        WebhookSigner.GenerateSecret().ShouldNotBe(secret);

        var (token, hash, prefix) = ApiTokens.Generate();
        token.ShouldStartWith("hb_");
        token.Length.ShouldBe(3 + 43);
        prefix.ShouldBe(token[..10]);
        hash.ShouldBe(ApiTokens.Hash(token));
        hash.Length.ShouldBe(64);
    }
}
