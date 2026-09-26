namespace SystemCore.Core.SystemInformation;

// Aggregates independent providers; no Windows implementation or hardware assumptions.
public sealed class SystemInformationService(IOperatingSystemInfoProvider os, ICpuInfoProvider cpu,
    IMemoryInfoProvider memory, IGpuInfoProvider gpu, IMotherboardInfoProvider firmware,
    IStorageInfoProvider storage, INetworkInfoProvider network) : ISystemInformationService
{
    public async Task<SystemInformationSnapshot> CollectAsync(CancellationToken token)
    {
        var o = Read(os, token); var c = Read(cpu, token); var m = Read(memory, token);
        var g = Read(gpu, token); var f = Read(firmware, token); var s = Read(storage, token); var n = Read(network, token);
        await Task.WhenAll(o, c, m, g, f, s, n).ConfigureAwait(false);
        return new(DateTimeOffset.Now, await o, await c, await m, await g, await f, await s, await n);
    }

    private static async Task<SectionResult<T>> Read<T>(IInfoProvider<T> provider, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        try
        {
            var data = await provider.CollectAsync(timeout.Token).WaitAsync(timeout.Token).ConfigureAwait(false);
            return new(data, CollectionStatus.Ready);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (OperationCanceledException) { return new(default, CollectionStatus.TimedOut); }
        catch (Exception ex)
        {
            // Do not log payload, exception messages, host names, addresses or identifiers.
            System.Diagnostics.Trace.TraceWarning("Provider {0}: {1} (0x{2:X8})", provider.GetType().Name, ex.GetType().Name, ex.HResult);
            return new(default, ex is UnauthorizedAccessException ? CollectionStatus.PermissionRequired : CollectionStatus.Unavailable);
        }
    }
}
