using SystemCore.Core.Startup;

namespace SystemCore.Infrastructure.Startup;

public sealed class StartupInventoryService(IRegistryStartupProvider registry, IStartupFolderProvider folders, ITaskSchedulerStartupProvider tasks,
    IStartupStateService? state = null, IElevatedStartupClient? elevated = null) : IStartupInventoryService
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
            var capability = new StartupManageability(registry, folders, tasks, state as StartupStateService, elevated);
            var rows = await Task.Run(() => sources.SelectMany(s => s.Items).GroupBy(i => i.Id, StringComparer.Ordinal)
                .Select(g => { token.ThrowIfCancellationRequested(); var item = g.Last(); return item with { CanManage = capability.CanManage(item) }; })
                .OrderBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase).ToArray(), token).ConfigureAwait(false);
            var result = new StartupInventoryResult(Array.AsReadOnly(rows), sources.All(s => s.Complete), sources.SelectMany(s => s.UnavailableSources).Distinct().ToArray());
            session = new(Guid.NewGuid(), DateTimeOffset.UtcNow, result);
            var manageable = rows.Where(ManageableStartupItems.IsVisible).ToArray();
            Summary = new(manageable.Length, manageable.Count(i => i.Status == StartupStatus.Enabled), manageable.Count(i => i.Status is StartupStatus.DisabledBySystemCore or StartupStatus.DisabledByWindowsOrSource), rows.Count(i => i.IsProtected), result.Complete);
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
