using System.Net.NetworkInformation;
using System.Net.Sockets;
using SystemCore.Core.SystemInformation;

namespace SystemCore.Infrastructure.SystemInformation;

public sealed class WindowsNetworkInfoProvider : INetworkInfoProvider
{
    public async Task<IReadOnlyList<NetworkAdapterInfo>> CollectAsync(CancellationToken token)
    {
        var physical = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var metadata = await WmiReader.Query(@"root\cimv2", "SELECT GUID,PhysicalAdapter FROM Win32_NetworkAdapter", row =>
                (Id: WmiReader.Text(row, "GUID"), Physical: row["PhysicalAdapter"] as bool?), token);
            foreach (var item in metadata) if (item.Id is not null && item.Physical.HasValue) physical[item.Id] = item.Physical.Value;
        }
        catch (Exception ex) when (ex is System.Management.ManagementException or System.Runtime.InteropServices.COMException or UnauthorizedAccessException) { }
        var items = new List<NetworkAdapterInfo>();
        foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
        {
            token.ThrowIfCancellationRequested();
            var ipv4 = new List<string>(); var ipv6 = new List<string>(); var gateways = new List<string>(); var dns = new List<string>();
            long? speed = null;
            try
            {
                var properties = adapter.GetIPProperties();
                ipv4.AddRange(properties.UnicastAddresses.Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork).Select(a => a.Address.ToString()));
                ipv6.AddRange(properties.UnicastAddresses.Where(a => a.Address.AddressFamily == AddressFamily.InterNetworkV6).Select(a => a.Address.ToString()));
                gateways.AddRange(properties.GatewayAddresses.Select(a => a.Address.ToString()));
                dns.AddRange(properties.DnsAddresses.Select(a => a.ToString()));
                if (adapter.OperationalStatus == OperationalStatus.Up && adapter.Speed > 0) speed = adapter.Speed;
            }
            catch (NetworkInformationException) { }
            items.Add(new(adapter.Name, adapter.Description, adapter.NetworkInterfaceType.ToString(), adapter.OperationalStatus.ToString(),
                adapter.OperationalStatus == OperationalStatus.Up, physical.TryGetValue(adapter.Id, out var p) ? p :
                    adapter.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel ? false : null,
                speed, ipv4.AsReadOnly(), ipv6.AsReadOnly(), gateways.AsReadOnly(), dns.AsReadOnly()));
        }
        return items.OrderByDescending(a => a.Active).ThenBy(a => a.Physical == false).ThenBy(a => a.Name).ToArray();
    }
}
