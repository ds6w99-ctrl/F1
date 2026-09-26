using SystemCore.Core.Dashboard;

namespace SystemCore.Infrastructure.Dashboard;

internal static class WindowsVolumeReader
{
    internal static string SystemRoot => Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows))!;
    internal static VolumeUsage? Read(DriveInfo drive) => DashboardMath.Volume(drive.Name, drive.TotalSize, drive.TotalFreeSpace, drive.AvailableFreeSpace);
}
