using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;
using SystemCore.Core.Startup;
using SystemCore.Infrastructure.Cleaner;

namespace SystemCore.Infrastructure.Startup;

internal sealed record StartupHelperRequest(int Version, string Nonce, DateTimeOffset CreatedAt, string ItemId, string Fingerprint, StartupAction Action);
internal static class StartupElevationProtocol
{
    internal static bool Validate(StartupHelperRequest? request, string? nonce, DateTimeOffset now) => request is not null && request.Version == 1
        && StartupStateService.ValidId(request.Nonce) && request.Nonce == nonce && StartupStateService.ValidId(request.ItemId)
        && StartupStateService.ValidId(request.Fingerprint) && Enum.IsDefined(request.Action)
        && request.CreatedAt <= now.AddSeconds(10) && now - request.CreatedAt < TimeSpan.FromMinutes(2);
    internal static async Task WriteAsync<T>(Stream stream, T value, CancellationToken token)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value);
        if (bytes.Length is <= 0 or > 4096) throw new IOException("Invalid payload size");
        await stream.WriteAsync(BitConverter.GetBytes(bytes.Length), token).ConfigureAwait(false);
        await stream.WriteAsync(bytes, token).ConfigureAwait(false); await stream.FlushAsync(token).ConfigureAwait(false);
    }
    internal static async Task<T> ReadAsync<T>(Stream stream, CancellationToken token)
    {
        var header = new byte[4]; await stream.ReadExactlyAsync(header, token).ConfigureAwait(false);
        var length = BitConverter.ToInt32(header);
        if (length is <= 0 or > 4096) throw new IOException("Invalid payload size");
        var bytes = new byte[length]; await stream.ReadExactlyAsync(bytes, token).ConfigureAwait(false);
        return JsonSerializer.Deserialize<T>(bytes, new JsonSerializerOptions { UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow, MaxDepth = 8 })
            ?? throw new IOException("Invalid payload");
    }
}
public sealed class WindowsElevatedStartupClient : IElevatedStartupClient
{
    public async Task<StartupOperationResult> ApplyAsync(string itemId, string fingerprint, StartupAction action, CancellationToken token)
    {
        var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var request = new StartupHelperRequest(1, nonce, DateTimeOffset.UtcNow, itemId, fingerprint, action);
        if (!StartupElevationProtocol.Validate(request, nonce, DateTimeOffset.UtcNow)) return new(StartupOutcome.RefreshRequired);
        var sent = false;
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        using var connecting = CancellationTokenSource.CreateLinkedTokenSource(token, deadline.Token);
        var name = "SystemCore-Startup-" + Guid.NewGuid().ToString("N");
        using var pipe = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        try
        {
            token.ThrowIfCancellationRequested();
            using var launch = new HelperLaunchLease();
            using var helper = Process.Start(launch.StartInfo(true, name, Environment.ProcessId, nonce)) ?? throw new IOException("Helper unavailable");
            await pipe.WaitForConnectionAsync(connecting.Token).ConfigureAwait(false);
            if (!CleanerPipePeer.GetNamedPipeClientProcessId(pipe.SafePipeHandle, out var pid) || pid != helper.Id) throw new IOException("Wrong helper peer");
            token.ThrowIfCancellationRequested();
            await StartupElevationProtocol.WriteAsync(pipe, request, deadline.Token).ConfigureAwait(false); sent = true;
            // Once dispatched, finish this one transaction and retain its result.
            // Cancellation is honored before dispatch, not by killing the helper.
            var result = await StartupElevationProtocol.ReadAsync<StartupOperationResult>(pipe, deadline.Token).ConfigureAwait(false);
            return Enum.IsDefined(result.Outcome) ? result : new(StartupOutcome.RecoveryRequired);
        }
        catch (System.ComponentModel.Win32Exception e) when (e.NativeErrorCode == 1223) { return new(StartupOutcome.Cancelled); }
        catch (OperationCanceledException) { return new(sent ? StartupOutcome.RecoveryRequired : StartupOutcome.Cancelled); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or JsonException)
        { return new(sent ? StartupOutcome.RecoveryRequired : StartupOutcome.Failed); }
    }
}
public static class StartupHelperHost
{
    public static async Task<int> RunAsync(string[] args)
    {
        const string prefix = "SystemCore-Startup-";
        if (args.Length != 4 || args[0] != "--startup" || !args[1].StartsWith(prefix, StringComparison.Ordinal)
            || !Guid.TryParseExact(args[1][prefix.Length..], "N", out _) || !int.TryParse(args[2], out var parentId)
            || !StartupStateService.ValidId(args[3])) return 64;
        using var identity = WindowsIdentity.GetCurrent();
        if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator)) return 5;
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        try
        {
            using var parent = Process.GetProcessById(parentId);
            var expected = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", Core.ProductIdentity.AppExecutableName));
            if (!string.Equals(parent.MainModule?.FileName, expected, StringComparison.OrdinalIgnoreCase)) return 5;
            using var pipe = new NamedPipeClientStream(".", args[1], PipeDirection.InOut, PipeOptions.Asynchronous, TokenImpersonationLevel.Identification);
            await pipe.ConnectAsync(timeout.Token).ConfigureAwait(false);
            if (!CleanerPipePeer.GetNamedPipeServerProcessId(pipe.SafePipeHandle, out var peer) || peer != parentId) return 5;
            var request = await StartupElevationProtocol.ReadAsync<StartupHelperRequest>(pipe, timeout.Token).ConfigureAwait(false);
            if (!StartupElevationProtocol.Validate(request, args[3], DateTimeOffset.UtcNow)) return 64;
            var registry = new RegistryStartupProvider(); var folders = new StartupFolderProvider(); var tasks = new TaskSchedulerStartupProvider();
            var state = new StartupStateService(); var inventory = new StartupInventoryService(registry, folders, tasks, state);
            var snapshot = await inventory.RefreshAsync(timeout.Token).ConfigureAwait(false);
            var item = snapshot.Inventory.Items.SingleOrDefault(i => i.Id == request.ItemId);
            var result = item is null || item.Fingerprint != request.Fingerprint ? new(StartupOutcome.RefreshRequired)
                : !item.RequiresElevation || item.IsProtected ? new StartupOperationResult(StartupOutcome.Protected)
                : await Task.Run(() => new StartupMutationService(inventory, registry, folders, tasks, state).Execute(item, request.Action, timeout.Token)).ConfigureAwait(false);
            await StartupElevationProtocol.WriteAsync(pipe, result, timeout.Token).ConfigureAwait(false);
            return 0;
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested) { return 65; }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Core.Diagnostics.ReliabilityDiagnostics.Log.UnexpectedFailure((int)Core.Diagnostics.ReliabilityComponent.Helper, ex.HResult);
            return 65;
        }
    }
}
