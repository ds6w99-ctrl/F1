namespace SystemCore.Core.Dashboard;

public static class DashboardMath
{
    // GetSystemTimes kernel includes idle, and already aggregates processors (<=64).
    public static double? CpuPercent(CpuTimes previous, CpuTimes current)
    {
        if (current.Idle < previous.Idle || current.Kernel < previous.Kernel || current.User < previous.User)
            return null;
        var idle = current.Idle - previous.Idle;
        var total = (double)(current.Kernel - previous.Kernel) + (current.User - previous.User);
        if (total <= 0 || idle > total) return null;
        return Math.Clamp((total - idle) * 100 / total, 0, 100);
    }

    public static MemoryUsage? Memory(ulong total, ulong available) =>
        total == 0 || available > total ? null : new(total - available, total);

    public static VolumeUsage? Volume(string root, long total, long free, long available) =>
        string.IsNullOrWhiteSpace(root) || total <= 0 || free < 0 || free > total || available < 0 || available > free
            ? null : new(root, (ulong)(total - free), (ulong)total, (ulong)available);

    public static bool ShouldMonitor(bool dashboardSelected, bool visible, bool minimized) =>
        dashboardSelected && visible && !minimized;

    // Deduplicate reflected registry views using stable registration identity, not display name alone.
    public static string DesktopIdentity(string registrationKey, string name, string version, string location) =>
        Guid.TryParse(registrationKey, out var productCode)
            ? $"msi:{productCode:D}"
            : $"desktop:{registrationKey.Trim()}|{name.Trim()}|{version.Trim()}|{location.Trim()}";
}
