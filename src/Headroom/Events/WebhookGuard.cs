using System.Net;
using System.Net.Sockets;
using Headroom.Client.Shell;

namespace Headroom.Events;

// Keeps a Webhook away from the internal network (SSRF). The URL check runs when a Planner saves the Webhook.
// The address check runs at connect time, so a DNS name that later points inside is blocked too.
public static class WebhookGuard
{
    public static string? UrlProblem(string? raw, bool development, out Uri url) => UrlRules.WebhookUrl(raw, development, out url);

    public static bool IsBlocked(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (IPAddress.IsLoopback(ip) || ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any)) return true;
        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
            return ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || (ip.GetAddressBytes()[0] & 0xFE) == 0xFC;
        var b = ip.GetAddressBytes();
        return b[0] == 10 || (b[0] == 172 && b[1] is >= 16 and <= 31) || (b[0] == 192 && b[1] == 168) || (b[0] == 169 && b[1] == 254);
    }

    // No redirects, so a receiver cannot send Headroom to another address. allowInternal is for Development.
    public static SocketsHttpHandler CreateHandler(bool allowInternal) => new()
    {
        AllowAutoRedirect = false,
        ConnectCallback = async (context, ct) =>
        {
            var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, ct);
            var allowed = allowInternal ? addresses : addresses.Where(a => !IsBlocked(a)).ToArray();
            if (allowed.Length == 0) throw new HttpRequestException("The Webhook address is not allowed.");
            var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(allowed[0], context.DnsEndPoint.Port, ct);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        },
    };
}
