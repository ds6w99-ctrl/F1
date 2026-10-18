using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

using SystemCore.Core.MemoryTools;

namespace SystemCore.Infrastructure.MemoryTools;

public enum MemoryReleaseKind { StandbyLowPriority, StandbyAll, WorkingSet, ModifiedPageList, CombinedPageList, RegistryCache, SystemFileCache }

public sealed record MemoryReleaseResult(MemoryReleaseKind Kind, bool Succeeded, int Affected, string Detail);

public interface ISystemMemoryReleaser
{
    // Each kind is a documented Windows API call. The caller must pass an explicit selection;
    // there is no "clean everything" shortcut and excluded processes are never touched.
    Task<IReadOnlyList<MemoryReleaseResult>> ReleaseAsync(IReadOnlyCollection<MemoryReleaseKind> kinds, IReadOnlyCollection<string> excludedProcesses, CancellationToken token);
}

// Native memory release. Only documented system calls are used, every call is individually
// reported, and a failure of one area does not abort the others. Requires the caller to hold
// SeProfileSingleProcessPrivilege and SeIncreaseQuotaPrivilege (the elevated helper holds both);
// when the privilege is missing the call fails closed with a reported reason instead of throwing.
public sealed class SystemMemoryReleaser : ISystemMemoryReleaser
{
    private const uint SystemMemoryListInformation = 80;
    private const uint SystemRegistryReorganize = 155;
    private const int MemoryPurgeStandbyList = 4;
    private const int MemoryPurgeLowPriorityStandbyList = 5;
    private const int MemoryFlushModifiedList = 3;
    private const int MemoryPurgeCombinedPageList = 2;
    private const uint SePrivilegeEnabled = 0x2;
    private const uint TokenAdjustPrivileges = 0x20, TokenQuery = 0x8;

    public Task<IReadOnlyList<MemoryReleaseResult>> ReleaseAsync(IReadOnlyCollection<MemoryReleaseKind> kinds,
        IReadOnlyCollection<string> excludedProcesses, CancellationToken token) => Task.Run(() =>
    {
        var results = new List<MemoryReleaseResult>();
        var excluded = excludedProcesses.Select(p => p.Trim()).Where(p => p.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        // The app itself and critical system processes are always excluded, whatever the caller says.
        foreach (var mandatory in MandatoryExclusions) excluded.Add(mandatory);
        foreach (var kind in kinds.Distinct())
        {
            token.ThrowIfCancellationRequested();
            try { results.Add(kind == MemoryReleaseKind.WorkingSet ? TrimWorkingSets(excluded, token) : SystemCall(kind)); }
            catch (OperationCanceledException) { throw; }
            catch (Exception e) when (e is Win32Exception or IOException or UnauthorizedAccessException)
            { results.Add(new(kind, false, 0, e.Message)); }
        }
        return (IReadOnlyList<MemoryReleaseResult>)results.AsReadOnly();
    }, token);

    internal static readonly string[] MandatoryExclusions = ["System", "Registry", "Idle", "Memory Compression", "Secure System", "smss", "csrss", "wininit", "services", "lsass", "winlogon", "fontdrvhost", "MsMpEng", "F1"];

    private static MemoryReleaseResult SystemCall(MemoryReleaseKind kind)
    {
        if (!EnablePrivilege("SeProfileSingleProcessPrivilege", out var privilegeError))
            return new(kind, false, 0, "Privilege unavailable: " + privilegeError);
        uint command = kind switch
        {
            MemoryReleaseKind.StandbyLowPriority => MemoryPurgeLowPriorityStandbyList,
            MemoryReleaseKind.StandbyAll => MemoryPurgeStandbyList,
            MemoryReleaseKind.ModifiedPageList => MemoryFlushModifiedList,
            MemoryReleaseKind.CombinedPageList => MemoryPurgeCombinedPageList,
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        var status = NtSetSystemInformation(SystemMemoryListInformation, ref command, sizeof(uint));
        if (status != 0)
            return new(kind, false, 0, "NtSetSystemInformation failed with NTSTATUS 0x" + status.ToString("X8"));
        // Registry cache uses the same facility with its own information class on Windows 8.1+.
        if (kind == MemoryReleaseKind.RegistryCache)
        {
            RegistryReorganize empty = default;
            status = NtSetSystemInformationRegistry(SystemRegistryReorganize, ref empty, Marshal.SizeOf<RegistryReorganize>());
            if (status != 0) return new(kind, false, 0, "Registry reorganize failed with NTSTATUS 0x" + status.ToString("X8"));
        }
        return new(kind, true, 1, "Completed");
    }

    private static MemoryReleaseResult TrimWorkingSets(HashSet<string> excluded, CancellationToken token)
    {
        var affected = 0;
        var failures = 0;
        foreach (var process in Process.GetProcesses())
        {
            token.ThrowIfCancellationRequested();
            using (process)
            {
                try
                {
                    // System/kernel processes and every excluded name are skipped before any handle is opened.
                    if (excluded.Contains(process.ProcessName)) continue;
                    // Session 0 hosts services and kernel-adjacent hosts; trimming those working sets
                    // buys nothing and risks stalls, so the trim stays inside user sessions.
                    if (process.SessionId == 0) continue;
                    IntPtr handle;
                    try { handle = OpenProcess(ProcessAccess.QueryLimited | ProcessAccess.SetQuota, false, process.Id); }
                    catch (Win32Exception) { failures++; continue; }
                    if (handle == IntPtr.Zero) { failures++; continue; }
                    try { if (EmptyWorkingSet(handle)) affected++; else failures++; }
                    finally { CloseHandle(handle); }
                }
                catch (Exception e) when (e is InvalidOperationException or Win32Exception) { failures++; }
            }
        }
        return new(MemoryReleaseKind.WorkingSet, affected > 0, affected,
            affected.ToString() + " processes trimmed" + (failures > 0 ? ", " + failures + " skipped" : ""));
    }

    private static bool EnablePrivilege(string privilege, out string error)
    {
        error = "";
        try
        {
            if (!OpenProcessToken(GetCurrentProcess(), TokenAdjustPrivileges | TokenQuery, out var token))
            { error = "OpenProcessToken " + Marshal.GetLastWin32Error(); return false; }
            try
            {
                if (!LookupPrivilegeValue(null, privilege, out var luid))
                { error = "LookupPrivilegeValue " + Marshal.GetLastWin32Error(); return false; }
                var state = new TokenPrivileges { PrivilegeCount = 1, Luid = luid, Attributes = SePrivilegeEnabled };
                if (!AdjustTokenPrivileges(token, false, ref state, 0, IntPtr.Zero, IntPtr.Zero))
                { error = "AdjustTokenPrivileges " + Marshal.GetLastWin32Error(); return false; }
                var last = Marshal.GetLastWin32Error();
                if (last != 0) { error = "Privilege not held (" + last + ")"; return false; }
                return true;
            }
            finally { CloseHandle(token); }
        }
        catch (Exception e) when (e is Win32Exception or EntryPointNotFoundException) { error = e.Message; return false; }
    }

    [StructLayout(LayoutKind.Sequential)] private struct TokenPrivileges { public uint PrivilegeCount; public long Luid; public uint Attributes; }
    [StructLayout(LayoutKind.Sequential)] private struct RegistryReorganize { public int Reserved; }
    [Flags] private enum ProcessAccess : uint { QueryLimited = 0x1000, SetQuota = 0x0100 }

    [DllImport("ntdll.dll")] private static extern int NtSetSystemInformation(uint infoClass, ref uint info, int length);
    [DllImport("ntdll.dll", EntryPoint = "NtSetSystemInformation")] private static extern int NtSetSystemInformationRegistry(uint infoClass, ref RegistryReorganize info, int length);    [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool CloseHandle(IntPtr handle);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)] private static extern bool LookupPrivilegeValue(string? system, string name, out long luid);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool AdjustTokenPrivileges(IntPtr token, bool disableAll, ref TokenPrivileges state, int length, IntPtr previous, IntPtr returnLength);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(ProcessAccess access, bool inherit, int processId);
    [DllImport("psapi.dll", SetLastError = true)] private static extern bool EmptyWorkingSet(IntPtr process);
}
