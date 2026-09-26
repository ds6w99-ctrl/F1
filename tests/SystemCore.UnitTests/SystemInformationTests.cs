using System.Globalization;
using SystemCore.Core.SystemInformation;

internal static class SystemInformationTests
{
    public static async Task<bool> Run()
    {
        var passed = true;
        void Check(string name, bool value) { Console.WriteLine($"{(value ? "PASS" : "FAIL")}: {name}"); passed &= value; }
        var culture = CultureInfo.InvariantCulture;
        foreach (var (value, expected) in new (ulong, string)[] { (0, "0 B"), (42, "42 B"), (1048576, "1.0 MB"),
            (1073741824, "1.0 GB"), (1099511627776, "1.0 TB"), (ulong.MaxValue, "16,777,216.0 TB") })
            Check($"Binary size {value}", InfoFormatting.Size(value, culture) == expected);
        Check("Percent zero", InfoFormatting.Percentage(0, 100) == 0);
        Check("Percent full", InfoFormatting.Percentage(100, 100) == 100);
        Check("Percent normal", InfoFormatting.Percentage(25, 100) == 25);
        Check("Percent zero denominator", InfoFormatting.Percentage(0, 0) is null);
        Check("Percent invalid range", InfoFormatting.Percentage(101, 100) is null);
        Check("Null manufacturer stays absent", InfoFormatting.Clean(null) is null);
        Check("Empty model stays absent", InfoFormatting.Clean("  ") is null);
        Check("Firmware placeholder stays absent", InfoFormatting.Clean("To be filled by O.E.M.") is null);
        Check("Real hardware model preserved", InfoFormatting.Clean(" Example model ") == "Example model");
        var fake = new Providers();
        var snapshot = await Service(fake).CollectAsync(default);
        Check("Multiple GPUs retained", snapshot.Gpu.Data?.Count == 2);
        Check("Multiple disks retained", snapshot.Storage.Data?.Disks.Count == 2);
        Check("Multiple RAM modules retained", snapshot.Memory.Data?.Count == 2);
        Check("Multiple adapters retained", snapshot.Network.Data?.Count == 2);
        Check("Missing GPU VRAM not fabricated", snapshot.Gpu.Data![0].DedicatedBytes is null);
        Check("Missing RAM speed not fabricated", snapshot.Memory.Data![0].ConfiguredSpeed is null);
        Check("Missing BIOS fields retained", snapshot.Firmware.Data!.Bios[0].Version is null);
        foreach (var failed in new[] { "os", "cpu", "memory", "gpu", "firmware", "storage", "network" })
        {
            var provider = new Providers { Fail = failed };
            var s = await Service(provider).CollectAsync(default);
            CollectionStatus[] states = [s.OperatingSystem.Status, s.Cpu.Status, s.Memory.Status, s.Gpu.Status, s.Firmware.Status, s.Storage.Status, s.Network.Status];
            Check($"Independent {failed} failure", states.Count(v => v == CollectionStatus.Ready) == 6 && states.Count(v => v == CollectionStatus.Unavailable) == 1);
        }
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        try { await Service(fake).CollectAsync(cancel.Token); Check("Cancellation propagates", false); }
        catch (OperationCanceledException) { Check("Cancellation propagates", true); }
        return passed;
    }
    private static SystemInformationService Service(Providers p) => new(p, p, p, p, p, p, p);
    private sealed class Providers : IOperatingSystemInfoProvider, ICpuInfoProvider, IMemoryInfoProvider,
        IGpuInfoProvider, IMotherboardInfoProvider, IStorageInfoProvider, INetworkInfoProvider
    {
        public string? Fail { get; init; }
        private Task<T> Return<T>(string name, T value, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (Fail == name) throw new InvalidOperationException("Test-only provider fault");
            return Task.FromResult(value);
        }
        Task<OperatingSystemInfo> IInfoProvider<OperatingSystemInfo>.CollectAsync(CancellationToken t) => Return("os", new OperatingSystemInfo("Windows 11", null, null, null, "X64", "test", null, null, null), t);
        Task<IReadOnlyList<CpuInfo>> IInfoProvider<IReadOnlyList<CpuInfo>>.CollectAsync(CancellationToken t) => Return<IReadOnlyList<CpuInfo>>("cpu", [new(null, "test", "x64", 1, 1, null)], t);
        Task<IReadOnlyList<MemoryModuleInfo>> IInfoProvider<IReadOnlyList<MemoryModuleInfo>>.CollectAsync(CancellationToken t) => Return<IReadOnlyList<MemoryModuleInfo>>("memory", [new(null, 1, null, null, null, null, null, null), new(null, 2, null, null, null, null, null, null)], t);
        Task<IReadOnlyList<GpuInfo>> IInfoProvider<IReadOnlyList<GpuInfo>>.CollectAsync(CancellationToken t) => Return<IReadOnlyList<GpuInfo>>("gpu", [new("test A", null, null, null, null, null), new("test B", null, null, null, null, null)], t);
        Task<FirmwareInfo> IInfoProvider<FirmwareInfo>.CollectAsync(CancellationToken t) => Return("firmware", new FirmwareInfo([], [new(null, null, null, null)]), t);
        Task<StorageInfo> IInfoProvider<StorageInfo>.CollectAsync(CancellationToken t) => Return("storage", new StorageInfo([new(null, null, null, null, null, null), new(null, null, null, null, null, null)], [], false), t);
        Task<IReadOnlyList<NetworkAdapterInfo>> IInfoProvider<IReadOnlyList<NetworkAdapterInfo>>.CollectAsync(CancellationToken t) => Return<IReadOnlyList<NetworkAdapterInfo>>("network", [new("A", "", "", "", false, null, null, [], [], [], []), new("B", "", "", "", true, null, null, [], [], [], [])], t);
    }
}
