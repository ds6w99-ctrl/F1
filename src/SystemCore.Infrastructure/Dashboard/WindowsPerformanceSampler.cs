using System.Runtime.InteropServices;
using SystemCore.Core.Dashboard;

namespace SystemCore.Infrastructure.Dashboard;

// Called by a single worker, never on the UI thread. No process enumeration or WMI.
public sealed class WindowsPerformanceSampler : IPerformanceSampler
{
    private CpuTimes? previous;
    private readonly string systemRoot = WindowsVolumeReader.SystemRoot;
    private readonly bool cpuSupported = Native.GetActiveProcessorCount(0xffff) is > 0 and <= 64;
    private int volumeTicks;
    private Reading<VolumeUsage> volume = Reading<VolumeUsage>.Missing();

    public void ResetCpuBaseline() => previous = null;

    public DashboardSample Sample()
    {
        var cpu = Reading<double>.Missing(cpuSupported ? ReadStatus.Unavailable : ReadStatus.Unsupported);
        if (cpuSupported && Native.GetSystemTimes(out var idle, out var kernel, out var user))
        {
            var now = new CpuTimes(idle.Value, kernel.Value, user.Value);
            var value = previous is { } before ? DashboardMath.CpuPercent(before, now) : null;
            cpu = value.HasValue ? Reading<double>.Ready(value.Value) : Reading<double>.Missing(ReadStatus.WarmingUp);
            previous = now;
        }
        else previous = null;

        var memory = Reading<MemoryUsage>.Missing();
        var nativeMemory = new Native.MemoryStatus { Length = (uint)Marshal.SizeOf<Native.MemoryStatus>() };
        if (Native.GlobalMemoryStatusEx(ref nativeMemory) &&
            DashboardMath.Memory(nativeMemory.TotalPhysical, nativeMemory.AvailablePhysical) is { } usage)
            memory = Reading<MemoryUsage>.Ready(usage);

        // Capacity is inexpensive but need not be queried every second.
        if (volumeTicks++ % 5 == 0)
        {
            try
            {
                var drive = new DriveInfo(systemRoot);
                var data = WindowsVolumeReader.Read(drive);
                volume = data.HasValue ? Reading<VolumeUsage>.Ready(data.Value) : Reading<VolumeUsage>.Missing();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                volume = Reading<VolumeUsage>.Missing(ex is UnauthorizedAccessException ? ReadStatus.PermissionRequired : ReadStatus.Unavailable);
            }
        }
        return new(DateTimeOffset.Now, cpu, memory, volume, TimeSpan.FromMilliseconds(Native.GetTickCount64()));
    }

    private static class Native
    {
        [StructLayout(LayoutKind.Sequential)]
        internal struct FileTime
        {
            public uint Low;
            public uint High;
            public readonly ulong Value => ((ulong)High << 32) | Low;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct MemoryStatus
        {
            public uint Length, Load;
            public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile;
            public ulong TotalVirtual, AvailableVirtual, AvailableExtendedVirtual;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetSystemTimes(out FileTime idle, out FileTime kernel, out FileTime user);
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
        [DllImport("kernel32.dll")]
        internal static extern ulong GetTickCount64();
        [DllImport("kernel32.dll")]
        internal static extern uint GetActiveProcessorCount(ushort groupNumber);
    }
}
