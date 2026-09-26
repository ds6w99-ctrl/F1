using Microsoft.Win32;
using System.Globalization;
using System.Runtime.InteropServices;
using SystemCore.Core.SystemInformation;

namespace SystemCore.Infrastructure.SystemInformation;

public sealed class WindowsOperatingSystemInfoProvider : IOperatingSystemInfoProvider
{
    // CurrentVersion registry fields report installed Windows identity independently of app manifest.
    // ProductName can retain "Windows 10" on Windows 11; derive name from CurrentBuildNumber.
    public static OperatingSystemInfo ReadIdentity()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
        var build = key?.GetValue("CurrentBuildNumber") as string;
        var ubr = key?.GetValue("UBR");
        return new(int.TryParse(build, out var b) && b >= 22000 ? "Windows 11" : null,
            key?.GetValue("EditionID") as string, key?.GetValue("DisplayVersion") as string,
            build is null ? null : ubr is null ? build : $"{build}.{ubr}",
            RuntimeInformation.OSArchitecture.ToString(), Environment.MachineName, null,
            CultureInfo.InstalledUICulture.Name, CultureInfo.CurrentUICulture.Name);
    }
    public async Task<OperatingSystemInfo> CollectAsync(CancellationToken token)
    {
        var identity = await Task.Run(ReadIdentity, token).ConfigureAwait(false);
        try
        {
            var dates = await WmiReader.Query(@"root\cimv2", "SELECT InstallDate,Locale FROM Win32_OperatingSystem", row =>
                (Date: WmiReader.Date(row, "InstallDate"), Locale: WmiReader.Text(row, "Locale")), token).ConfigureAwait(false);
            var data = dates.FirstOrDefault();
            string? locale = null;
            if (int.TryParse(data.Locale, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var id))
                try { locale = CultureInfo.GetCultureInfo(id).Name; } catch (CultureNotFoundException) { }
            return identity with { Installed = data.Date, SystemLocale = locale };
        }
        catch (Exception ex) when (ex is System.Management.ManagementException or COMException or UnauthorizedAccessException)
        { return identity with { SystemLocale = null }; }
    }
}
