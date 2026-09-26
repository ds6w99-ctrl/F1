using System.Diagnostics;
using System.Runtime.InteropServices;
using SystemCore.Core.Dashboard;
using SystemCore.Core.MemoryTools;

namespace SystemCore.Infrastructure.MemoryTools;

public sealed class WindowsMemoryMetricsProvider : IMemoryMetricsProvider
{
    public MemorySnapshot Read(DashboardSample basic)
    {
        ulong? commit = null, limit = null, cache = null, working = null, privateBytes = null;
        if (GetPerformanceInfo(out var info, Marshal.SizeOf<PerformanceInfo>()))
        {
            commit = MemoryMetrics.Pages((ulong)info.CommitTotal, (ulong)info.PageSize);
            limit = MemoryMetrics.Pages((ulong)info.CommitLimit, (ulong)info.PageSize);
            cache = MemoryMetrics.Pages((ulong)info.SystemCache, (ulong)info.PageSize);
            if (MemoryMetrics.Percent(commit, limit) is null) { commit = null; limit = null; }
        }
        try
        {
            using var current = Process.GetCurrentProcess();
            if (current.WorkingSet64 >= 0) working = (ulong)current.WorkingSet64;
            if (current.PrivateMemorySize64 >= 0) privateBytes = (ulong)current.PrivateMemorySize64;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException) { }
        var physical = basic.Memory.Value;
        var valid = physical is { TotalBytes: > 0 } && physical.Value.UsedBytes <= physical.Value.TotalBytes;
        return new(basic.Timestamp, valid ? physical!.Value.TotalBytes : null, valid ? physical!.Value.TotalBytes - physical.Value.UsedBytes : null,
            commit, limit, cache, working, privateBytes);
    }
    [StructLayout(LayoutKind.Sequential)] private struct PerformanceInfo
    {
        public uint Size;
        public UIntPtr CommitTotal, CommitLimit, CommitPeak, PhysicalTotal, PhysicalAvailable, SystemCache, KernelTotal, KernelPaged, KernelNonpaged, PageSize;
        public uint Handles, Processes, Threads;
    }
    [DllImport("psapi.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetPerformanceInfo(out PerformanceInfo info, int size);
}
