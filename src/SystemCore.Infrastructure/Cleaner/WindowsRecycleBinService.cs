using System.Runtime.InteropServices;
using SystemCore.Core.Cleaner;

namespace SystemCore.Infrastructure.Cleaner;

public interface IRecycleBinShell
{
    RecycleBinSnapshot Query();
    bool Empty();
}

// Shell owns Recycle Bin semantics. No filesystem enumeration/deletion is used here.
public sealed class WindowsRecycleBinService(IRecycleBinShell shell) : IRecycleBinService
{
    public Task<RecycleBinSnapshot> QueryAsync(CancellationToken token) => OnSta(() =>
    {
        if (token.IsCancellationRequested) return new(false, 0, 0);
        return shell.Query();
    });
    public Task<RecycleBinOutcome> EmptyAsync(RecycleBinSnapshot expected, CancellationToken token) => OnSta(() =>
    {
        if (token.IsCancellationRequested) return RecycleBinOutcome.Cancelled;
        if (!expected.Available) return RecycleBinOutcome.Unavailable;
        var current = shell.Query();
        if (!current.Available) return RecycleBinOutcome.Unavailable;
        if (current != expected) return RecycleBinOutcome.Changed;
        if (token.IsCancellationRequested) return RecycleBinOutcome.Cancelled;
        // Once Shell starts, it cannot be cancelled/rolled back by this application.
        return shell.Empty() ? RecycleBinOutcome.Completed : RecycleBinOutcome.Failed;
    });
    private static Task<T> OnSta<T>(Func<T> action)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var initialized = CoInitializeEx(IntPtr.Zero, 2) >= 0;
            try { if (!initialized) throw new InvalidOperationException("Shell COM initialization failed"); completion.SetResult(action()); }
            catch (Exception ex) { completion.SetException(ex); }
            finally { if (initialized) CoUninitialize(); }
        }) { IsBackground = true, Name = "SystemCore Recycle Bin" };
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); return completion.Task;
    }
    [DllImport("ole32.dll")] private static extern int CoInitializeEx(IntPtr reserved, uint mode);
    [DllImport("ole32.dll")] private static extern void CoUninitialize();
}

public sealed class WindowsRecycleBinShell : IRecycleBinShell
{
    public RecycleBinSnapshot Query()
    {
        var info = new QueryInfo { Size = (uint)Marshal.SizeOf<QueryInfo>() };
        var hr = SHQueryRecycleBin(null, ref info);
        return hr >= 0 && info.Items >= 0 && info.Bytes >= 0 ? new(true, info.Items, info.Bytes) : new(false, 0, 0);
    }
    public bool Empty() => SHEmptyRecycleBin(IntPtr.Zero, null, 1 | 2 | 4) >= 0;
    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    private struct QueryInfo { public uint Size; public long Bytes; public long Items; }
    [DllImport("shell32.dll", EntryPoint = "SHQueryRecycleBinW", CharSet = CharSet.Unicode)]
    private static extern int SHQueryRecycleBin(string? root, ref QueryInfo info);
    [DllImport("shell32.dll", EntryPoint = "SHEmptyRecycleBinW", CharSet = CharSet.Unicode)]
    private static extern int SHEmptyRecycleBin(IntPtr window, string? root, uint flags);
}
