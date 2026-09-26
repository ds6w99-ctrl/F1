using System.Diagnostics;
using System.Text.Json;
using SystemCore.Core.SystemInformation;
using SystemCore.Infrastructure.Dashboard;
using SystemCore.Infrastructure.SystemInformation;

var service = new SystemInformationService(new WindowsOperatingSystemInfoProvider(), new WindowsCpuInfoProvider(),
    new WindowsMemoryInfoProvider(), new WindowsGpuInfoProvider(), new WindowsMotherboardInfoProvider(),
    new WindowsStorageInfoProvider(), new WindowsNetworkInfoProvider());
using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
var watch = Stopwatch.StartNew();
var snapshot = await service.CollectAsync(cancellation.Token);
watch.Stop();
var telemetry = new WindowsPerformanceSampler().Sample();
var checks = new (string Name, bool Pass)[]
{
    ("OS provider returns Windows 11", snapshot.OperatingSystem.Data?.Name == "Windows 11"),
    ("CPU model populated", snapshot.Cpu.Data?.Any(c => !string.IsNullOrWhiteSpace(c.Model)) == true),
    ("Total physical RAM positive", telemetry.Memory.Value?.TotalBytes > 0),
    ("System volume present", snapshot.Storage.Data?.Volumes.Any(v => v.IsSystem && v.Total > 0) == true),
    ("Network collection completes", snapshot.Network.Status == CollectionStatus.Ready),
    ("Snapshot contains independent sections", snapshot.Collected != default && Enum.IsDefined(snapshot.Cpu.Status) && Enum.IsDefined(snapshot.Storage.Status))
};
foreach (var (name, pass) in checks) Console.WriteLine($"{(pass ? "PASS" : "FAIL")}: {name}");
// Deliberately omit hostname, IP/DNS/gateway/MAC, device identifiers and serial numbers.
Console.WriteLine(JsonSerializer.Serialize(new
{
    CollectionMilliseconds = watch.Elapsed.TotalMilliseconds,
    Windows = new { snapshot.OperatingSystem.Data?.Name, snapshot.OperatingSystem.Data?.Edition, snapshot.OperatingSystem.Data?.Version, snapshot.OperatingSystem.Data?.Build },
    Cpu = snapshot.Cpu.Data, Memory = telemetry.Memory.Value, Modules = snapshot.Memory.Data,
    Gpu = snapshot.Gpu.Data, Firmware = snapshot.Firmware.Data,
    PhysicalDisks = snapshot.Storage.Data?.Disks,
    Volumes = snapshot.Storage.Data?.Volumes.Select(v => new { v.Root, v.FileSystem, v.Total, v.Free, v.IsSystem }),
    Network = snapshot.Network.Data?.Select(n => new { n.Description, n.Type, n.Status, n.Physical, n.Speed, Ipv4Count = n.Ipv4.Count, Ipv6Count = n.Ipv6.Count }),
    Statuses = new { Os = snapshot.OperatingSystem.Status, Cpu = snapshot.Cpu.Status, Memory = snapshot.Memory.Status,
        Gpu = snapshot.Gpu.Status, Firmware = snapshot.Firmware.Status, Storage = snapshot.Storage.Status, Network = snapshot.Network.Status }
}, new JsonSerializerOptions { WriteIndented = true }));
return checks.All(c => c.Pass) ? 0 : 1;
