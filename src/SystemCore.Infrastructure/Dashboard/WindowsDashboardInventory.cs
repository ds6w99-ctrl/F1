using System.Runtime.InteropServices;
using System.Security;
using Microsoft.Win32;
using SystemCore.Core.Dashboard;
using Windows.Management.Deployment;

namespace SystemCore.Infrastructure.Dashboard;

public sealed class WindowsDashboardInventory : IDashboardInventoryProvider
{
    public MachineOverview ReadMachine()
    {
        var os = SystemInformation.WindowsOperatingSystemInfoProvider.ReadIdentity();
        return new(os.DeviceName, $"{os.Name} {os.Edition} {os.Version} · {os.Build}");
    }

    public InventoryCounts ReadCounts(CancellationToken cancellationToken)
    {
        var apps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var appsPartial = false;
        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var root = RegistryKey.OpenBaseKey(hive, view);
                using var uninstall = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                if (uninstall is not null)
                foreach (var name in uninstall.GetSubKeyNames())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        using var item = uninstall.OpenSubKey(name);
                        if (item?.GetValue("DisplayName") is not string title || string.IsNullOrWhiteSpace(title) ||
                            item.GetValue("SystemComponent") is int flag && flag == 1) continue;
                        apps.Add(DashboardMath.DesktopIdentity(name, title,
                            item.GetValue("DisplayVersion") as string ?? "",
                            item.GetValue("InstallLocation") as string ?? ""));
                    }
                    catch (Exception ex) when (IsReadFailure(ex)) { appsPartial = true; }
                }
            }
            catch (Exception ex) when (IsReadFailure(ex)) { appsPartial = true; }

        }

        try
        {
            // Only the current user's packages; never enumerate another user's profile.
            foreach (var package in new PackageManager().FindPackagesForUser(string.Empty))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!package.IsFramework && !package.IsResourcePackage && !package.IsBundle)
                    apps.Add($"msix:{package.Id.FamilyName}");
            }
        }
        catch (Exception ex) when (IsReadFailure(ex)) { appsPartial = true; }

        return new(new(apps.Count, appsPartial ? ReadStatus.Partial : ReadStatus.Ready),
            Reading<int>.Missing(ReadStatus.WarmingUp)); // Normalized Startup service owns this count.
    }

    private static bool IsReadFailure(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or SecurityException or COMException;
}
