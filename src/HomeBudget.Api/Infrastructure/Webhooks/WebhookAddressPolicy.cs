using System.Net;
using System.Net.Sockets;

namespace HomeBudget.Api.Infrastructure.Webhooks;

/// <summary>
/// Adresses joignables par un webhook (I5), vérifiées après la résolution DNS, au moment de la connexion : une
/// adresse refusée ne peut pas être atteinte par un nom de domaine qui la désigne. Toujours refusées : boucle
/// locale, link-local (dont les métadonnées cloud 169.254.169.254), adresses non spécifiées, multicast et
/// diffusion. Les réseaux privés sont autorisés par défaut (domotique, réseau Docker).
/// </summary>
public static class WebhookAddressPolicy
{
    private static readonly (IPAddress Network, int Prefix)[] AlwaysDenied =
    [
        (IPAddress.Parse("0.0.0.0"), 8),
        (IPAddress.Parse("127.0.0.0"), 8),
        (IPAddress.Parse("169.254.0.0"), 16),
        (IPAddress.Parse("224.0.0.0"), 4),
        (IPAddress.Parse("240.0.0.0"), 4),
        (IPAddress.Parse("::"), 128),
        (IPAddress.Parse("::1"), 128),
        (IPAddress.Parse("fe80::"), 10),
        (IPAddress.Parse("ff00::"), 8),
    ];

    private static readonly (IPAddress Network, int Prefix)[] Private =
    [
        (IPAddress.Parse("10.0.0.0"), 8),
        (IPAddress.Parse("172.16.0.0"), 12),
        (IPAddress.Parse("192.168.0.0"), 16),
        (IPAddress.Parse("100.64.0.0"), 10),
        (IPAddress.Parse("fc00::"), 7),
    ];

    public static bool IsAllowed(IPAddress address, bool allowPrivateNetworks)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }
        if (Array.Exists(AlwaysDenied, n => Contains(n, address)))
        {
            return false;
        }
        return allowPrivateNetworks || !Array.Exists(Private, n => Contains(n, address));
    }

    private static bool Contains((IPAddress Network, int Prefix) range, IPAddress address) =>
        range.Network.AddressFamily == address.AddressFamily
        && new IPNetwork(range.Network, range.Prefix).Contains(address);

    /// <summary>
    /// Connexion du client HTTP des webhooks : résout le nom, écarte les adresses refusées et se connecte à la
    /// première adresse autorisée.
    /// </summary>
    public static async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, bool allowPrivateNetworks, CancellationToken ct)
    {
        var host = context.DnsEndPoint.Host;
        var addresses = IPAddress.TryParse(host, out var literal) ? [literal] : await Dns.GetHostAddressesAsync(host, ct);
        var allowed = addresses.Where(a => IsAllowed(a, allowPrivateNetworks)).ToList();
        if (allowed.Count == 0)
        {
            throw new WebhookAddressException($"Adresse refusée pour un webhook : {host}.");
        }
        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync([.. allowed], context.DnsEndPoint.Port, ct);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
}

public sealed class WebhookAddressException(string message) : Exception(message);
