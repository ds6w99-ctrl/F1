using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Security.Principal;
using SystemCore.Core.Uninstaller;
using SystemCore.Infrastructure.Cleaner;

namespace SystemCore.Infrastructure.Uninstaller;

// Elevated filesystem leftover removal, used only for items the unprivileged pass could
// not touch. Identity is carried in-process; the helper re-derives every decision.
internal interface IElevatedLeftoverClient
{
    Task<IReadOnlyList<LeftoverDeletionItemResult>> DeleteAsync(InstalledApplication application, IReadOnlyList<ScannedLeftover> items, CancellationToken token);
}

internal sealed class WindowsElevatedLeftoverClient : IElevatedLeftoverClient
{
    public async Task<IReadOnlyList<LeftoverDeletionItemResult>> DeleteAsync(InstalledApplication application, IReadOnlyList<ScannedLeftover> items, CancellationToken token)
    {
        LeftoverDeletionItemResult[] Reject(LeftoverDeletionStatus reason) => items.Select(i => new LeftoverDeletionItemResult(i.Item.Id, reason, 0)).ToArray();
        if (token.IsCancellationRequested) return Reject(LeftoverDeletionStatus.Cancelled);
        if (items.Count is 0 or > LeftoverElevationProtocol.MaximumItems || items.Any(i => i.Identity is null)) return Reject(LeftoverDeletionStatus.SafetyRejected);
        var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var request = new ElevatedLeftoverRequest(LeftoverElevationProtocol.Version, nonce, Guid.NewGuid(), DateTimeOffset.UtcNow, application.Id,
            application.DisplayName, application.Publisher, application.InstallLocation, application.ItemKind,
            items.Select(i => new ElevatedLeftoverItem(i.Item.Id, i.Item.Type, i.Path, i.Root, i.Identity!)).ToArray());
        if (!LeftoverElevationProtocol.Validate(request, nonce, DateTimeOffset.UtcNow) || !LeftoverElevationProtocol.FitsPayload(request)) return Reject(LeftoverDeletionStatus.SafetyRejected);
        var name = "SystemCore-Leftover-" + Guid.NewGuid().ToString("N");
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        using var pipe = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        var sent = false;
        try
        {
            // The only child operation is the fixed application helper through the Windows
            // runas verb. No command interpreter, external utility or path argument.
            using var launch = new HelperLaunchLease();
            using var helper = Process.Start(launch.StartInfo("leftover", name, Environment.ProcessId, nonce)) ?? throw new IOException("Helper unavailable");
            using var connecting = CancellationTokenSource.CreateLinkedTokenSource(token, timeout.Token);
            await pipe.WaitForConnectionAsync(connecting.Token).ConfigureAwait(false);
            if (!CleanerPipePeer.GetNamedPipeClientProcessId(pipe.SafePipeHandle, out var pid) || pid != helper.Id) throw new IOException("Unexpected helper peer");
            token.ThrowIfCancellationRequested();
            await LeftoverElevationProtocol.WriteAsync(pipe, request, timeout.Token).ConfigureAwait(false);
            sent = true;
            // A single cancel byte requests cooperative stop. Wait for structured partial
            // results instead of discarding evidence of already completed deletion.
            using var registration = token.Register(() => { try { pipe.WriteByte(1); pipe.Flush(); } catch (IOException) { } catch (ObjectDisposedException) { } });
            var response = await LeftoverElevationProtocol.ReadAsync<ElevatedLeftoverResponse>(pipe, timeout.Token).ConfigureAwait(false);
            if (response.Version != LeftoverElevationProtocol.Version || !response.Accepted || response.Items.Length != items.Count) return Reject(LeftoverDeletionStatus.Failed);
            var expected = items.ToDictionary(i => i.Item.Id);
            if (response.Items.Select(i => i.ItemId).Distinct().Count() != items.Count || response.Items.Any(r =>
                !expected.TryGetValue(r.ItemId, out var item) || !Enum.IsDefined(r.Outcome)
                || r.RemovedBytes != (r.Outcome == ElevatedLeftoverOutcome.Deleted && item.Item.Type == LeftoverType.File ? item.Identity!.Size : 0)))
                return Reject(LeftoverDeletionStatus.Failed);
            return response.Items.Select(r => new LeftoverDeletionItemResult(r.ItemId, Map(r.Outcome), r.RemovedBytes)).ToArray();
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) { return Reject(LeftoverDeletionStatus.Cancelled); }
        catch (OperationCanceledException) { return Reject(sent ? LeftoverDeletionStatus.Failed : LeftoverDeletionStatus.Cancelled); }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Core.Diagnostics.ReliabilityDiagnostics.Log.UnexpectedFailure((int)Core.Diagnostics.ReliabilityComponent.Helper, ex.HResult);
            return Reject(sent ? LeftoverDeletionStatus.Failed : LeftoverDeletionStatus.Failed);
        }
    }
    private static LeftoverDeletionStatus Map(ElevatedLeftoverOutcome outcome) => outcome switch
    {
        ElevatedLeftoverOutcome.Deleted => LeftoverDeletionStatus.Deleted,
        ElevatedLeftoverOutcome.Changed => LeftoverDeletionStatus.Changed,
        ElevatedLeftoverOutcome.SafetyRejected => LeftoverDeletionStatus.SafetyRejected,
        ElevatedLeftoverOutcome.AccessDenied => LeftoverDeletionStatus.AccessDenied,
        ElevatedLeftoverOutcome.InUse => LeftoverDeletionStatus.InUse,
        ElevatedLeftoverOutcome.AlreadyGone => LeftoverDeletionStatus.AlreadyGone,
        ElevatedLeftoverOutcome.Skipped => LeftoverDeletionStatus.Skipped,
        ElevatedLeftoverOutcome.Cancelled => LeftoverDeletionStatus.Cancelled,
        _ => LeftoverDeletionStatus.Failed
    };
}

public static class LeftoverHelperHost
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length != 4 || args[0] != "--leftover" || !args[1].StartsWith("SystemCore-Leftover-", StringComparison.Ordinal) ||
            !Guid.TryParseExact(args[1][20..], "N", out _) || !int.TryParse(args[2], out var parentId) ||
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
            var request = await LeftoverElevationProtocol.ReadAsync<ElevatedLeftoverRequest>(pipe, timeout.Token).ConfigureAwait(false);
            using var monitorStop = new CancellationTokenSource();
            var monitor = MonitorCancel(pipe, stop, monitorStop.Token);
            ElevatedLeftoverResponse response;
            try { response = await Task.Run(() => LeftoverElevationProtocol.Execute(request, args[3], stop.Token), CancellationToken.None).ConfigureAwait(false); }
            finally { monitorStop.Cancel(); await monitor.ConfigureAwait(false); }
            await LeftoverElevationProtocol.WriteAsync(pipe, response, timeout.Token).ConfigureAwait(false);
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
