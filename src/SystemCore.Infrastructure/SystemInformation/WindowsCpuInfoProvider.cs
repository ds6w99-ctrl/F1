using SystemCore.Core.SystemInformation;

namespace SystemCore.Infrastructure.SystemInformation;

public sealed class WindowsCpuInfoProvider : ICpuInfoProvider
{
    public Task<IReadOnlyList<CpuInfo>> CollectAsync(CancellationToken token) => WmiReader.Query(@"root\cimv2",
        "SELECT Manufacturer,Name,Architecture,NumberOfCores,NumberOfLogicalProcessors FROM Win32_Processor", row =>
            new CpuInfo(WmiReader.Text(row, "Manufacturer"), WmiReader.Text(row, "Name"),
                WmiReader.Number(row, "Architecture") switch { 9 => "x64", 12 => "ARM64", 0 => "x86", _ => null },
                WmiReader.Number(row, "NumberOfCores"), WmiReader.Number(row, "NumberOfLogicalProcessors"), null), token);
    // MaxClockSpeed is not guaranteed base clock; intentionally do not relabel it as such.
}
