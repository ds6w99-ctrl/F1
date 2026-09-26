using SystemCore.Core.SystemInformation;

namespace SystemCore.Infrastructure.SystemInformation;

public sealed class WindowsStorageInfoProvider : IStorageInfoProvider
{
    public async Task<StorageInfo> CollectAsync(CancellationToken token)
    {
        IReadOnlyList<PhysicalDiskInfo> disks = [];
        var partial = false;
        try
        {
            disks = await WmiReader.Query(@"root\Microsoft\Windows\Storage",
                "SELECT Model,Manufacturer,BusType,MediaType,Size,HealthStatus FROM MSFT_PhysicalDisk", row =>
                    new PhysicalDiskInfo(WmiReader.Text(row, "Model"), WmiReader.Text(row, "Manufacturer"),
                        WmiReader.Number(row, "BusType"), WmiReader.Number(row, "MediaType"), WmiReader.Bytes(row, "Size"), WmiReader.Number(row, "HealthStatus")), token);
        }
        catch (Exception ex) when (ex is System.Management.ManagementException or System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
        { partial = true; }
        var volumes = new List<VolumeInfo>();
        var systemRoot = Dashboard.WindowsVolumeReader.SystemRoot;
        foreach (var drive in DriveInfo.GetDrives())
        {
            token.ThrowIfCancellationRequested();
            // Local mounted drive-letter volumes only. Do not contact mapped network shares.
            if (drive.DriveType == DriveType.Network) continue;
            try
            {
                if (!drive.IsReady) { volumes.Add(new(drive.Name, null, null, null, null, drive.Name == systemRoot)); continue; }
                var usage = Dashboard.WindowsVolumeReader.Read(drive);
                volumes.Add(new(drive.Name, drive.VolumeLabel, drive.DriveFormat, usage?.TotalBytes,
                    usage is { } value ? value.TotalBytes - value.UsedBytes : null, drive.Name.Equals(systemRoot, StringComparison.OrdinalIgnoreCase)));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { partial = true; volumes.Add(new(drive.Name, null, null, null, null, drive.Name == systemRoot)); }
        }
        return new(disks, volumes.AsReadOnly(), partial);
    }
}
