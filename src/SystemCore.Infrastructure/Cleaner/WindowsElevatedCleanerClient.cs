using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;
using SystemCore.Core.Cleaner;

namespace SystemCore.Infrastructure.Cleaner;

public sealed class WindowsElevatedCleanerClient : IElevatedCleanerClient
{
    public async Task<IReadOnlyList<CleanerItemResult>> CleanAsync(Guid session, DateTimeOffset scannedAt, IReadOnlyList<CleanerItem> items, CancellationToken token)
    {
        CleanerItemResult[] Reject(CleanerItemOutcome reason) => items.Select(i => new CleanerItemResult(i.Id, reason, 0)).ToArray();
        if (token.IsCancellationRequested) return Reject(CleanerItemOutcome.Cancelled);
        if (items.Count is 0 or > CleanerElevationProtocol.MaximumItems) return Reject(CleanerItemOutcome.SafetyRejected);
        var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var request = new ElevatedCleanerRequest(1, nonce, session, scannedAt,
            items.Select(i => new ElevatedCleanerItem(i.Id, i.Category, i.RelativePath, i.Identity)).ToArray());
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp");
        if (!CleanerElevationProtocol.Validate(request, nonce, root, DateTimeOffset.UtcNow) || !CleanerElevationProtocol.FitsPayload(request)) return Reject(CleanerItemOutcome.SafetyRejected);
        var name = "SystemCore-Cleaner-" + Guid.NewGuid().ToString("N");
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        using var pipe = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        var sent = false;
        try
        {
            // The only child operation is the fixed application helper through the Windows
            // runas verb. No command interpreter, external cleanup utility or path argument.
            using var launch = new HelperLaunchLease();
            using var helper = Process.Start(launch.StartInfo(false, name, Environment.ProcessId, nonce)) ?? throw new IOException("Helper unavailable");
            using var connecting = CancellationTokenSource.CreateLinkedTokenSource(token, timeout.Token);
            await pipe.WaitForConnectionAsync(connecting.Token).ConfigureAwait(false);
            if (!CleanerPipePeer.GetNamedPipeClientProcessId(pipe.SafePipeHandle, out var pid) || pid != helper.Id) throw new IOException("Unexpected helper peer");
            token.ThrowIfCancellationRequested();
            await CleanerElevationProtocol.WriteAsync(pipe, request, timeout.Token).ConfigureAwait(false);
            sent = true;
            // A single cancel byte requests cooperative stop. Wait for structured partial
            // results instead of discarding evidence of already completed deletion.
            using var registration = token.Register(() => { try { pipe.WriteByte(1); pipe.Flush(); } catch (IOException) { } catch (ObjectDisposedException) { } });
            var response = await CleanerElevationProtocol.ReadAsync<ElevatedCleanerResponse>(pipe, timeout.Token).ConfigureAwait(false);
            if (response.Version != 1 || !response.Accepted || response.Items.Length != items.Count) return Reject(CleanerItemOutcome.Unknown);
            var expected = items.ToDictionary(i => i.Id);
            if (response.Items.Select(i => i.ItemId).Distinct().Count() != items.Count || response.Items.Any(r =>
                !expected.TryGetValue(r.ItemId, out var item) || !Enum.IsDefined(r.Outcome) || r.RemovedBytes != (r.Outcome == CleanerItemOutcome.Deleted ? item.Size : 0))) return Reject(CleanerItemOutcome.Unknown);
            return response.Items;
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223) { return Reject(CleanerItemOutcome.Cancelled); }
        catch (OperationCanceledException) { return Reject(sent ? CleanerItemOutcome.Unknown : CleanerItemOutcome.Cancelled); }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Core.Diagnostics.ReliabilityDiagnostics.Log.UnexpectedFailure((int)Core.Diagnostics.ReliabilityComponent.Helper, ex.HResult);
            return Reject(sent ? CleanerItemOutcome.Unknown : CleanerItemOutcome.Failed);
        }
    }
}

public static class CleanerHelperHost
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length != 4 || args[0] != "--cleaner" || !args[1].StartsWith("SystemCore-Cleaner-", StringComparison.Ordinal) ||
            !Guid.TryParseExact(args[1][19..], "N", out _) || !int.TryParse(args[2], out var parentId) ||
            args[3].Length != 64 || args[3].Any(c => !Uri.IsHexDigit(c))) return 64;
        using var identity = WindowsIdentity.GetCurrent();
        if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator)) return 5;
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        try
        {
            using var parent = Process.GetProcessById(parentId);
            var expected = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", Core.ProductIdentity.AppExecutableName));
            if (!string.Equals(parent.MainModule?.FileName, expected, StringComparison.OrdinalIgnoreCase)) return 5;
            using var pipe = new NamedPipeClientStream(".", args[1], PipeDirection.InOut, PipeOptions.Asynchronous, TokenImpersonationLevel.Identification);
            await pipe.ConnectAsync(timeout.Token).ConfigureAwait(false);
            if (!CleanerPipePeer.GetNamedPipeServerProcessId(pipe.SafePipeHandle, out var peer) || peer != parentId) return 5;
            var request = await CleanerElevationProtocol.ReadAsync<ElevatedCleanerRequest>(pipe, timeout.Token).ConfigureAwait(false);
            using var monitorStop = new CancellationTokenSource();
            var monitor = MonitorCancel(pipe, stop, monitorStop.Token);
            ElevatedCleanerResponse response;
            try { response = await Task.Run(() => CleanerElevationProtocol.Execute(request, args[3], stop.Token), CancellationToken.None).ConfigureAwait(false); }
            finally { monitorStop.Cancel(); await monitor.ConfigureAwait(false); }
            await CleanerElevationProtocol.WriteAsync(pipe, response, timeout.Token).ConfigureAwait(false);
            return response.Accepted ? 0 : 64;
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { return 65; }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            stop.Cancel();
            Core.Diagnostics.ReliabilityDiagnostics.Log.UnexpectedFailure((int)Core.Diagnostics.ReliabilityComponent.Helper, ex.HResult);
            return 65;
        }
    }
    private static async Task MonitorCancel(Stream pipe, CancellationTokenSource stop, CancellationToken token)
    {
        try { var buffer = new byte[1]; await pipe.ReadAsync(buffer, token).ConfigureAwait(false); stop.Cancel(); }
        catch (OperationCanceledException) { }
        catch (IOException) { stop.Cancel(); }
    }
}
internal static class CleanerPipePeer
{
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out uint processId);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint processId);
}
