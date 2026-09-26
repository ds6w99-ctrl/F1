namespace SystemCore.Core.Tools;

public enum WindowsToolId { TaskManager, DeviceManager, DiskManagement, SystemInformation, Services, EventViewer, WindowsUpdate, NetworkConnections, InstalledApps, StorageSettings, SystemRestore, WindowsSecurity, PowerOptions, OptionalFeatures }
public enum ToolGroup { System, Storage, Network, Apps, Security, Recovery }
public enum ToolLaunchType { SystemExecutable, WindowsUri }
public enum ToolAvailability { Checking, Available, Unavailable }
public enum ToolLaunchResult { Launched, Unavailable, AccessDenied, Failed, Cancelled }
public sealed record WindowsToolItem(WindowsToolId Id, ToolGroup Group, string Icon, ToolLaunchType LaunchType,
    string Target, IReadOnlyList<string> Arguments, bool RequiresElevation, ToolAvailability Availability = ToolAvailability.Checking)
{
    public string NameKey => "Tool" + Id;
    public string DescriptionKey => NameKey + "Description";
}
public interface IWindowsToolsService
{
    Task<IReadOnlyList<WindowsToolItem>> ReadAsync(CancellationToken token);
    Task<ToolLaunchResult> OpenAsync(WindowsToolId id, CancellationToken token);
}
public static class WindowsToolCatalog
{
    public static IReadOnlyList<WindowsToolItem> Create(string systemDirectory)
    {
        if (!Path.IsPathFullyQualified(systemDirectory)) throw new ArgumentException("Absolute system directory required", nameof(systemDirectory));
        WindowsToolItem Exe(WindowsToolId id, ToolGroup group, string icon, string file, bool admin = false, string? argumentFile = null) =>
            new(id, group, icon, ToolLaunchType.SystemExecutable, Path.Combine(systemDirectory, file),
                argumentFile is null ? [] : Array.AsReadOnly(new[] { Path.Combine(systemDirectory, argumentFile) }), admin);
        WindowsToolItem Uri(WindowsToolId id, ToolGroup group, string icon, string uri) => new(id, group, icon, ToolLaunchType.WindowsUri, uri, [], false);
        return Array.AsReadOnly(new[]
        {
            Exe(WindowsToolId.TaskManager, ToolGroup.System, "\uE9D9", "Taskmgr.exe"),
            Exe(WindowsToolId.DeviceManager, ToolGroup.System, "\uE950", "mmc.exe", true, "devmgmt.msc"),
            Exe(WindowsToolId.DiskManagement, ToolGroup.Storage, "\uEDA2", "mmc.exe", true, "diskmgmt.msc"),
            Exe(WindowsToolId.SystemInformation, ToolGroup.System, "\uE946", "msinfo32.exe"),
            Exe(WindowsToolId.Services, ToolGroup.System, "\uE713", "mmc.exe", true, "services.msc"),
            Exe(WindowsToolId.EventViewer, ToolGroup.System, "\uE9F9", "mmc.exe", true, "eventvwr.msc"),
            Uri(WindowsToolId.WindowsUpdate, ToolGroup.System, "\uE895", "ms-settings:windowsupdate"),
            Exe(WindowsToolId.NetworkConnections, ToolGroup.Network, "\uE968", "control.exe", false, "ncpa.cpl"),
            Uri(WindowsToolId.InstalledApps, ToolGroup.Apps, "\uE71D", "ms-settings:appsfeatures"),
            Uri(WindowsToolId.StorageSettings, ToolGroup.Storage, "\uEDA2", "ms-settings:storagesense"),
            Exe(WindowsToolId.SystemRestore, ToolGroup.Recovery, "\uE777", "rstrui.exe", true),
            Uri(WindowsToolId.WindowsSecurity, ToolGroup.Security, "\uE83D", "windowsdefender:"),
            Exe(WindowsToolId.PowerOptions, ToolGroup.System, "\uE7E8", "control.exe", false, "powercfg.cpl"),
            Uri(WindowsToolId.OptionalFeatures, ToolGroup.Apps, "\uE710", "ms-settings:optionalfeatures")
        });
    }
}
