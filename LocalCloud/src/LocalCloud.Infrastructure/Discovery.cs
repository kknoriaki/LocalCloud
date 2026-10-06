using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Makaretu.Dns;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
namespace LocalCloud.Infrastructure;

public sealed class Discovery(SettingsStore settings, ILogger<Discovery> log) : IHostedService, IDisposable
{
    MulticastService? multicast; ServiceDiscovery? discovery;
    public bool Active { get; private set; }
    public static bool Private(IPAddress ip) { if (IPAddress.IsLoopback(ip)) return true; if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4(); var b = ip.GetAddressBytes(); return b.Length == 4 && (b[0] == 10 || (b[0] == 192 && b[1] == 168) || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 169 && b[1] == 254)) || b.Length == 16 && ((b[0] & 0xfe) == 0xfc || b[0] == 0xfe && (b[1] & 0xc0) == 0x80); }
    public static string[] Addresses(int port) { try { return NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up)
            .OrderBy(n => n.NetworkInterfaceType is NetworkInterfaceType.Ethernet or NetworkInterfaceType.Wireless80211 ? 0 : 1)
            .ThenBy(n => new[] { "virtual", "hyper-v", "vmware", "virtualbox", "vpn", "tap", "tunnel", "tailscale", "wireguard", "zerotier", "wsl" }.Any(word => (n.Name + " " + n.Description).Contains(word, StringComparison.OrdinalIgnoreCase)) ? 1 : 0)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses).Select(a => a.Address).Where(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a) && Private(a)).Select(a => $"http://{a}:{port}").Distinct().ToArray(); } catch (NetworkInformationException) { try { return Dns.GetHostAddresses(Dns.GetHostName()).Where(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a) && Private(a)).Select(a => $"http://{a}:{port}").ToArray(); } catch (SocketException) { return Array.Empty<string>(); } } }
    public Task StartAsync(CancellationToken ct) { if (!settings.Current.AllowLan) return Task.CompletedTask; try { multicast = new MulticastService(); discovery = new ServiceDiscovery(multicast); var addresses = Addresses(settings.Current.Port).Select(a => IPAddress.Parse(new Uri(a).Host)).ToArray(); if (addresses.Length == 0) return Task.CompletedTask; var service = new ServiceProfile("LocalCloud", "_http._tcp", (ushort)settings.Current.Port, addresses); service.HostName = "cloud.local"; service.AddProperty("path", "/"); discovery.Advertise(service); multicast.Start(); Active = true; } catch (Exception e) { log.LogWarning(e, "mDNS unavailable; use LAN IP address"); } return Task.CompletedTask; }
    public Task StopAsync(CancellationToken ct) { Dispose(); return Task.CompletedTask; }
    public void Dispose() { discovery?.Dispose(); multicast?.Dispose(); Active = false; }
}
