using System.Runtime.InteropServices;
using SystemCore.Core.Performance;

namespace SystemCore.Infrastructure.Performance;

// English counter paths work independently of Windows display language. _Total is physical-disk
// aggregate I/O, not capacity and not a guessed mapping of C: to a physical device.
public sealed class PdhDiskSampler : IDisposable
{
    private IntPtr query, read, write;
    private bool ready;
    public DiskPerformanceInfo Sample()
    {
        if (query == IntPtr.Zero)
        {
            if (PdhOpenQuery(null, IntPtr.Zero, out query) != 0) return new(null, null);
            if (PdhAddEnglishCounter(query, @"\PhysicalDisk(_Total)\Disk Read Bytes/sec", IntPtr.Zero, out read) != 0 ||
                PdhAddEnglishCounter(query, @"\PhysicalDisk(_Total)\Disk Write Bytes/sec", IntPtr.Zero, out write) != 0)
            { Dispose(); return new(null, null); }
        }
        if (PdhCollectQueryData(query) != 0) { ready = false; return new(null, null); }
        if (!ready) { ready = true; return new(null, null); }
        return new(Value(read), Value(write));
    }
    private static double? Value(IntPtr counter)
    {
        var result = PdhGetFormattedCounterValue(counter, 0x200, out _, out var value);
        return result == 0 && value.Status is 0 or 1 && double.IsFinite(value.Value) && value.Value >= 0 ? value.Value : null;
    }
    public void Dispose()
    {
        if (query != IntPtr.Zero) { PdhCloseQuery(query); query = IntPtr.Zero; }
        ready = false;
    }
    [StructLayout(LayoutKind.Explicit, Size = 16)]
    private struct CounterValue { [FieldOffset(0)] public uint Status; [FieldOffset(8)] public double Value; }
    [DllImport("pdh.dll", EntryPoint = "PdhOpenQueryW", CharSet = CharSet.Unicode)]
    private static extern uint PdhOpenQuery(string? source, IntPtr user, out IntPtr query);
    [DllImport("pdh.dll", EntryPoint = "PdhAddEnglishCounterW", CharSet = CharSet.Unicode)]
    private static extern uint PdhAddEnglishCounter(IntPtr query, string path, IntPtr user, out IntPtr counter);
    [DllImport("pdh.dll")] private static extern uint PdhCollectQueryData(IntPtr query);
    [DllImport("pdh.dll")] private static extern uint PdhGetFormattedCounterValue(IntPtr counter, uint format, out uint type, out CounterValue value);
    [DllImport("pdh.dll")] private static extern uint PdhCloseQuery(IntPtr query);
}
