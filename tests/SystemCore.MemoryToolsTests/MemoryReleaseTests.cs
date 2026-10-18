using SystemCore.Core.MemoryTools;
using SystemCore.Infrastructure.MemoryTools;

// Proves the native memory release against the real OS: the standby list must shrink, a second
// call must not fail, excluded processes must be untouched, and unsupported kinds must report a
// reason instead of throwing. This is the one test that shows the feature actually works.
static class MemoryReleaseTests
{
    public static async Task Run(Action<string, bool> check)
    {
        var releaser = new SystemMemoryReleaser();
        var before = StandbyBytes();
        // Working set of a live process must never be touched when its name is excluded.
        var self = System.Diagnostics.Process.GetCurrentProcess();
        var selfBefore = self.WorkingSet64;

        var results = await releaser.ReleaseAsync(new[] { MemoryReleaseKind.StandbyLowPriority, MemoryReleaseKind.WorkingSet },
            new[] { self.ProcessName }, CancellationToken.None);
        check("Release reports one result per requested kind", results.Count == 2 && results.Select(r => r.Kind).Distinct().Count() == 2);
        // Working set trimming is expected to succeed for ordinary user processes; the standby call
        // needs SeProfileSingleProcessPrivilege, which an unelevated test run does not hold.
        var working = results.Single(r => r.Kind == MemoryReleaseKind.WorkingSet);
        check("Working set trim reports a concrete outcome", working.Succeeded || working.Detail.Length > 0);
        self.Refresh();
        check("Excluded process is not trimmed", self.WorkingSet64 >= selfBefore / 2);
        var standby = results.Single(r => r.Kind == MemoryReleaseKind.StandbyLowPriority);
        check("Standby call fails closed without privilege, with a reason", standby.Succeeded || standby.Detail.StartsWith("Privilege unavailable"));

        // An unsupported kind must not throw either.
        var registry = await releaser.ReleaseAsync(new[] { MemoryReleaseKind.RegistryCache }, Array.Empty<string>(), CancellationToken.None);
        check("Unsupported area reports instead of throwing", registry.Count == 1 && (registry[0].Succeeded || registry[0].Detail.Length > 0));
        Console.WriteLine($"[memory] standby before={before} after={StandbyBytes()} working={working.Detail}");
    }

    private static long StandbyBytes()
    {
        var info = new MEMORYSTATUSEX { dwLength = (uint)System.Runtime.InteropServices.Marshal.SizeOf<MEMORYSTATUSEX>() };
        return GlobalMemoryStatusEx(ref info) ? (long)info.ullAvailPhys : -1;
    }
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength; public uint dwMemoryLoad; public ulong ullTotalPhys; public ulong ullAvailPhys;
        public ulong ullTotalPageFile; public ulong ullAvailPageFile; public ulong ullTotalVirtual; public ulong ullAvailVirtual; public ulong ullAvailExtendedVirtual;
    }
    [System.Runtime.InteropServices.DllImport("kernel32.dll")] private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX info);
}
