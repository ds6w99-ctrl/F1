using SystemCore.Core.SystemInformation;

namespace SystemCore.Infrastructure.SystemInformation;

public sealed class WindowsMotherboardInfoProvider : IMotherboardInfoProvider
{
    public async Task<FirmwareInfo> CollectAsync(CancellationToken token)
    {
        IReadOnlyList<MotherboardInfo> boards = [];
        IReadOnlyList<BiosInfo> bios = [];
        bool boardsUnavailable = false, biosUnavailable = false;
        try
        {
        boards = await WmiReader.Query(@"root\cimv2", "SELECT Manufacturer,Product,Version FROM Win32_BaseBoard", row =>
            new MotherboardInfo(WmiReader.Text(row, "Manufacturer"), WmiReader.Text(row, "Product"), WmiReader.Text(row, "Version")), token);
        }
        catch (Exception ex) when (IsSourceFailure(ex)) { boardsUnavailable = true; }
        try
        {
        bios = await WmiReader.Query(@"root\cimv2", "SELECT Manufacturer,SMBIOSBIOSVersion,ReleaseDate,SMBIOSMajorVersion,SMBIOSMinorVersion FROM Win32_BIOS", row =>
            new BiosInfo(WmiReader.Text(row, "Manufacturer"), WmiReader.Text(row, "SMBIOSBIOSVersion"), WmiReader.Date(row, "ReleaseDate"),
                WmiReader.Number(row, "SMBIOSMajorVersion") is > 0 and var major && WmiReader.Number(row, "SMBIOSMinorVersion") is { } minor ? $"{major}.{minor}" : null), token);
        }
        catch (Exception ex) when (IsSourceFailure(ex)) { biosUnavailable = true; }
        return new(boards, bios, boardsUnavailable, biosUnavailable);
    }
    private static bool IsSourceFailure(Exception ex) => ex is System.Management.ManagementException or
        System.Runtime.InteropServices.COMException or UnauthorizedAccessException;
}
