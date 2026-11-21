using System.Net;

namespace HomeBudget.Api.Infrastructure.Security;

/// <summary>Liste de réseaux CIDR (« 172.20.0.0/16, 10.0.0.0/8 ») avec test d'appartenance.</summary>
public sealed class IpNetworkList
{
    private readonly IReadOnlyList<IPNetwork> _networks;

    private IpNetworkList(IReadOnlyList<IPNetwork> networks) => _networks = networks;

    public IReadOnlyList<IPNetwork> Networks => _networks;

    public static IpNetworkList Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return new IpNetworkList([]);
        }

        var networks = new List<IPNetwork>();
        foreach (var part in value.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!IPNetwork.TryParse(part, out var network))
            {
                throw new FormatException($"Réseau invalide : « {part} » (format attendu : 172.20.0.0/16).");
            }
            networks.Add(network);
        }
        return new IpNetworkList(networks);
    }

    public bool Contains(IPAddress? address)
    {
        if (address is null)
        {
            return false;
        }
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }
        return _networks.Any(n => n.Contains(address));
    }
}
