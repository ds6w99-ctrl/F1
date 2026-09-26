using SystemCore.Core.SystemInformation;

namespace SystemCore.Infrastructure.SystemInformation;

public sealed class WindowsGpuInfoProvider : IGpuInfoProvider
{
    public Task<IReadOnlyList<GpuInfo>> CollectAsync(CancellationToken token) => WmiReader.Query(@"root\cimv2",
        "SELECT Name,AdapterCompatibility,VideoProcessor,DriverVersion,DriverDate FROM Win32_VideoController", row =>
            new GpuInfo(WmiReader.Text(row, "Name"), WmiReader.Text(row, "AdapterCompatibility"),
                WmiReader.Text(row, "VideoProcessor"), WmiReader.Text(row, "DriverVersion"), WmiReader.Date(row, "DriverDate"), null), token);
    // AdapterRAM is a uint32 and incorrect for many >4GiB adapters. Never query or display it.
    // No reliable DXGI adapter correlation implemented in this phase: dedicated VRAM stays unavailable.
}
