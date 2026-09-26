using SystemCore.Core.SystemInformation;

namespace SystemCore.Infrastructure.SystemInformation;

public sealed class WindowsMemoryInfoProvider : IMemoryInfoProvider
{
    public Task<IReadOnlyList<MemoryModuleInfo>> CollectAsync(CancellationToken token) => WmiReader.Query(@"root\cimv2",
        "SELECT Manufacturer,Capacity,ConfiguredClockSpeed,Speed,SMBIOSMemoryType,DeviceLocator,BankLabel,PartNumber FROM Win32_PhysicalMemory", row =>
            new MemoryModuleInfo(WmiReader.Text(row, "Manufacturer"), WmiReader.Bytes(row, "Capacity"),
                Positive(WmiReader.Number(row, "ConfiguredClockSpeed")), Positive(WmiReader.Number(row, "Speed")),
                WmiReader.Number(row, "SMBIOSMemoryType"), WmiReader.Text(row, "DeviceLocator"),
                WmiReader.Text(row, "BankLabel"), WmiReader.Text(row, "PartNumber")), token);
    private static uint? Positive(uint? value) => value > 0 ? value : null;
}
