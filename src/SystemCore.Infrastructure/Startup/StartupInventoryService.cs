using SystemCore.Core.Startup;

namespace SystemCore.Infrastructure.Startup;

public sealed class StartupInventoryService(IRegistryStartupProvider registry, IStartupFolderProvider folders, ITaskSchedulerStartupProvider tasks,
    IStartupStateService? state = null) : IStartupInventoryService
{
    internal readonly SemaphoreSlim Operation = new(1, 1);
    private StartupInventorySession? session;
    public StartupSummary? Summary { get; private set; }
    public event EventHandler? Changed;
    public async Task<StartupInventorySession> RefreshAsync(CancellationToken token)
    {
        await Operation.WaitAsync(token).ConfigureAwait(false);
        try
        {
            session = null;
            var sources = await Task.WhenAll(registry.ReadAsync(token), folders.ReadAsync(token), tasks.ReadAsync(token)).ConfigureAwait(false);
            for (var index = 0; index < sources.Length; index++) StartupDiagnostics.Log.InventorySource(index, sources[index].Items.Count, sources[index].Complete);
            if (state is not null) sources = [.. sources, await state.ReadAsync(token).ConfigureAwait(false)];
            var rows = sources.SelectMany(s => s.Items).GroupBy(i => i.Id, StringComparer.Ordinal).Select(g => g.Last()).OrderBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
            var result = new StartupInventoryResult(Array.AsReadOnly(rows), sources.All(s => s.Complete), sources.SelectMany(s => s.UnavailableSources).Distinct().ToArray());
            session = new(Guid.NewGuid(), DateTimeOffset.UtcNow, result);
            Summary = new(rows.Length, rows.Count(i => i.Status == StartupStatus.Enabled), rows.Count(i => i.Status is StartupStatus.DisabledBySystemCore or StartupStatus.DisabledByWindowsOrSource), rows.Count(i => i.IsProtected), result.Complete);
            Changed?.Invoke(this, EventArgs.Empty);
            return session;
        }
        finally { Operation.Release(); }
    }
    internal StartupItem? Consume(StartupOperationRequest request)
    {
        var current = session; session = null;
        return request.Confirmed && Enum.IsDefined(request.Action) && current?.Id == request.SessionId
            && DateTimeOffset.UtcNow - current.CreatedAt < TimeSpan.FromMinutes(10)
            ? current.Inventory.Items.SingleOrDefault(i => i.Id == request.ItemId) : null;
    }
}
