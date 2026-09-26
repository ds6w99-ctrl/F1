using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using SystemCore.Core.Performance;

namespace SystemCore.Infrastructure.Performance;

public sealed class WindowsProcessSampler
{
    private readonly ProcessDeltaTracker deltas = new();
    public IReadOnlyList<ProcessPerformanceInfo> Sample(double now, int processors, CancellationToken token)
    {
        var values = new List<ProcessCounter>();
        var processes = Process.GetProcesses();
        try
        {
            foreach (var process in processes)
            {
                token.ThrowIfCancellationRequested();
                string name;
                try { name = process.ProcessName; }
                catch (Exception ex) when (Expected(ex)) { continue; }
                long? start = null, cpu = null; ulong? memory = null, privateBytes = null;
                // Protected/exited processes are normal in every snapshot. Avoid
                // Process.StartTime/TotalProcessorTime's exception-driven access path.
                if (TryReadTimes(process.Id, out var creation, out var processorTime)) { start = creation; cpu = processorTime; }
                if (process.Id == 0) cpu = null; // Idle pseudo-process is not application CPU consumption.
                try { if (process.WorkingSet64 >= 0) memory = (ulong)process.WorkingSet64; }
                catch (Exception ex) when (Expected(ex)) { }
                try { if (process.PrivateMemorySize64 >= 0) privateBytes = (ulong)process.PrivateMemorySize64; }
                catch (Exception ex) when (Expected(ex)) { }
                values.Add(new(process.Id, start, name, cpu, memory, privateBytes));
            }
        }
        finally { foreach (var process in processes) process.Dispose(); }
        return deltas.Update(values, now, processors);
    }
    private static bool Expected(Exception ex) => ex is Win32Exception or InvalidOperationException or NotSupportedException;
    internal static bool TryReadTimes(int pid, out long start, out long cpu)
    {
        start = cpu = 0;
        if (pid <= 0) return false;
        using var process = OpenProcess(0x1000, false, pid); // QUERY_LIMITED_INFORMATION; never requests mutation rights.
        if (process.IsInvalid || !GetProcessTimes(process, out var created, out _, out var kernel, out var user)) return false;
        const long fileTimeEpochTicks = 504911232000000000;
        if (created <= 0 || created > DateTime.MaxValue.Ticks - fileTimeEpochTicks || kernel < 0 || user < 0 || kernel > long.MaxValue - user) return false;
        start = created + fileTimeEpochTicks; cpu = kernel + user;
        return true;
    }
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, int pid);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessTimes(SafeProcessHandle process, out long created, out long exited, out long kernel, out long user);
    public void Reset() => deltas.Reset();
}
