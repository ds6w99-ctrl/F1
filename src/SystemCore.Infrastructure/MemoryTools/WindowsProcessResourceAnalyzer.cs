using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using SystemCore.Core.MemoryTools;
using SystemCore.Infrastructure.Cleaner;

namespace SystemCore.Infrastructure.MemoryTools;

public sealed class WindowsProcessResourceAnalyzer : IProcessResourceAnalyzer
{
    public Task<MemoryProcessDetails?> ReadDetailsAsync(int pid, long startTicks, CancellationToken token) => Task.Run(() => Read(pid, startTicks, token), token);
    public Task<bool> OpenLocationAsync(int pid, long startTicks, CancellationToken token) => Task.Run(() =>
    {
        var details = Read(pid, startTicks, token); token.ThrowIfCancellationRequested();
        return details?.ExecutablePath is { } path && Startup.StartupFileLocation.Open(path);
    }, token);
    private static MemoryProcessDetails? Read(int pid, long startTicks, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); if (pid <= 0 || startTicks <= 0) return null;
        using var process = OpenProcess(0x1000, false, pid); // QUERY_LIMITED_INFORMATION only.
        if (process.IsInvalid || !GetProcessTimes(process, out var creation, out _, out _, out _) || DateTime.FromFileTimeUtc(creation).Ticks != startTicks) return null;
        var text = new StringBuilder(1024); var length = text.Capacity;
        if (!QueryFullProcessImageNameW(process, 0, text, ref length)) return null;
        var path = text.ToString(); string? publisher = null;
        try
        {
            using var lease = PinnedCleanerPath.Open(path, false, shareRead: true);
            publisher = FileVersionInfo.GetVersionInfo(path).CompanyName;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception) { path = ""; }
        token.ThrowIfCancellationRequested();
        if (!GetProcessTimes(process, out var verify, out var exit, out _, out _) || creation != verify || exit != 0) return null;
        return new(pid, startTicks, Path.GetFileNameWithoutExtension(text.ToString()), path.Length > 0 ? path : null, string.IsNullOrWhiteSpace(publisher) ? null : publisher);
    }
    [DllImport("kernel32.dll", SetLastError = true)] private static extern SafeProcessHandle OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, int pid);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetProcessTimes(SafeProcessHandle process, out long creation, out long exit, out long kernel, out long user);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool QueryFullProcessImageNameW(SafeProcessHandle process, uint flags, StringBuilder path, ref int size);
}
