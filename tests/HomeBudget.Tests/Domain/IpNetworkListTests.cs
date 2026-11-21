using System.Net;

using HomeBudget.Api.Infrastructure.Security;

namespace HomeBudget.Tests.Domain;

public sealed class IpNetworkListTests
{
    [Fact]
    public void Parses_multiple_networks()
    {
        var list = IpNetworkList.Parse("172.20.0.0/16, 10.0.0.0/8");

        list.Contains(IPAddress.Parse("172.20.3.4")).ShouldBeTrue();
        list.Contains(IPAddress.Parse("10.1.2.3")).ShouldBeTrue();
        list.Contains(IPAddress.Parse("192.168.1.1")).ShouldBeFalse();
        list.Contains(IPAddress.Parse("::ffff:172.20.0.9")).ShouldBeTrue();
        list.Contains(null).ShouldBeFalse();
    }

    [Fact]
    public void Empty_value_contains_nothing() =>
        IpNetworkList.Parse(null).Contains(IPAddress.Loopback).ShouldBeFalse();

    [Fact]
    public void Invalid_value_throws() =>
        Should.Throw<FormatException>(() => IpNetworkList.Parse("pas-un-réseau"));
}
