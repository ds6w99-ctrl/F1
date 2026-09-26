using System.Net.NetworkInformation;
using SystemCore.Core.Performance;

namespace SystemCore.Infrastructure.Performance;

public sealed class WindowsNetworkSampler
{
    private readonly NetworkDeltaTracker deltas = new();
    public NetworkPerformanceInfo Sample(double now)
    {
        var counters = new List<NetworkCounter>();
        // GetAllNetworkInterfaces includes protocol/filter interfaces on some Windows builds.
        // Keep active IP-bound adapters, excluding loopback; do not guess physical/VPN identity.
        foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (adapter.OperationalStatus != OperationalStatus.Up || adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
            try
            {
                if (adapter.GetIPProperties().UnicastAddresses.Count == 0) continue;
                var stats = adapter.GetIPStatistics();
                counters.Add(new(adapter.Id, adapter.Name, true, stats.BytesReceived, stats.BytesSent, adapter.Speed > 0 ? adapter.Speed : null));
            }
            catch (NetworkInformationException) { counters.Add(new(adapter.Id, adapter.Name, true, null, null, null)); }
        }
        return deltas.Update(counters, now);
    }
    public void Reset() => deltas.Reset();
}
