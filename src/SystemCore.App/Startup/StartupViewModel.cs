using SystemCore.App.Services;
using SystemCore.App.ViewModels;
using SystemCore.Core.Startup;
using SystemCore.Core.Orchestration;
using SystemCore.Core.Diagnostics;

namespace SystemCore.App.Startup;

public sealed class StartupViewModel : LocalizedViewModel
{
    // Cheap inventory; users expect toggles to be reflected promptly.
    public static readonly TimeSpan StartupFreshness = TimeSpan.FromMinutes(5);
    private readonly IStartupInventoryService inventory;
    private readonly IStartupMutationService mutation;
    private readonly IApplicationDataOrchestrator orchestrator;
    private readonly IAppDiagnostics diagnostics;
    private bool disposed;
    private Task? load;
    private StartupSummarySnapshot? cachedSummary;
    public StartupViewModel(LocalizationService localizer, IStartupInventoryService inventory, IStartupMutationService mutation,
        IApplicationDataOrchestrator orchestrator, IAppDiagnostics diagnostics) : base(localizer)
    {
        this.inventory = inventory; this.mutation = mutation; this.orchestrator = orchestrator; this.diagnostics = diagnostics;
        orchestrator.RegisterBackgroundWarmup(DataDataset.Startup, LoadTier.Background,
            async token => await ReadAsync(false, token), StartupFreshness);
    }
    // Read-only enumeration. The live service session is never persisted as a snapshot.
    private async Task<StartupInventorySession> ProduceAsync(CancellationToken token)
    {
        var value = await inventory.RefreshAsync(token).ConfigureAwait(false);
        var items = value.Inventory.Items;
        orchestrator.CacheStartupSummary(new(items.Count, items.Count(i => i.Status == StartupStatus.Enabled),
            items.Count(i => i.Status is StartupStatus.DisabledBySystemCore or StartupStatus.DisabledByWindowsOrSource),
            items.Count(i => i.IsProtected), value.Inventory.Complete, DateTimeOffset.UtcNow));
        return value;
    }
    private Task<StartupInventorySession> ReadAsync(bool force, CancellationToken token) => force
        ? orchestrator.RefreshAsync(DataDataset.Startup, LoadTier.Background, ProduceAsync, token)
        : orchestrator.GetAsync(DataDataset.Startup, LoadTier.Background, StartupFreshness, ProduceAsync, token);
    public Task RefreshIfStaleAsync() => orchestrator.IsStale(DataDataset.Startup, StartupFreshness) ? InitializeAsync() : Task.CompletedTask;
    public Task InitializeAsync() => StartLoad(false);
    public Task RefreshAsync() => StartLoad(true);
    private Task StartLoad(bool force) => disposed ? Task.CompletedTask
        : load is { IsCompleted: false } ? load : busy ? Task.CompletedTask : load = LoadAsync(force);
    private readonly CancellationTokenSource lifetime = new();
    private CancellationTokenSource? operation;
    private StartupInventorySession? session;
    private bool busy;
    private bool changing;
    private bool inventoryActionable;
    private string search = "", selected = "", message = "StartLoading";
    private int filter, sort, page;
    public string Text(string key) => L[key];
    public bool Busy => busy;
    public string StatusText => L[busy ? "StartLoading" : message] + (session is null && cachedSummary is { } cached ? $" — {cached.Total}" : "") + (!busy && session is { Inventory.Complete: true } current && Count == 0 ? "\n" + L[current.Inventory.Items.Count == 0 ? "StartEmpty" : "ProductNoMatches"] : "");
    public string Search { get => search; set { search = value; page = 0; selected = ""; Notify(); } }
    public int Filter { get => filter; set { filter = value; page = 0; selected = ""; Notify(); } }
    public int Sort { get => sort; set { sort = value; page = 0; selected = ""; Notify(); } }
    public string SelectedId { get => selected; set { selected = value; Notify(); } }
    public int RawCount => session?.Inventory.Items.Count ?? 0;
    public int ManageableCount => session?.Inventory.Items.Count(ManageableStartupItems.IsVisible) ?? 0;
    public int HiddenCount => RawCount - ManageableCount;
    public StartupItem? Selected => inventoryActionable ? session?.Inventory.Items.FirstOrDefault(i => i.Id == selected && ManageableStartupItems.IsVisible(i)) : null;
    public bool CanDisable => !busy && Selected is { IsProtected: false, Status: StartupStatus.Enabled };
    public bool CanEnable => !busy && Selected is { IsProtected: false, Status: StartupStatus.DisabledBySystemCore or StartupStatus.DisabledByWindowsOrSource or StartupStatus.RecoveryRequired };
    public bool CanCancel => busy && !changing;
    public string Confirmation(StartupAction action) => L[action == StartupAction.Disable ? "StartConfirmDisable" : "StartConfirmEnable"]
        + (Selected is { } item ? "\n\n" + item.Name + (item.RequiresElevation ? "\n" + L["StartConfirmElevation"] : "")
            + (action == StartupAction.Disable && item.SourceType == StartupSourceType.RegistryRunOnce ? "\n\n" + L["StartRunOnceWarning"] : "") : "");
    private IEnumerable<StartupItem> Filtered()
    {
        var items = (session?.Inventory.Items.AsEnumerable() ?? []).Where(ManageableStartupItems.IsVisible);
        items = items.Where(i => new[] { i.Name, i.Publisher, i.ExecutablePath, i.SourceLocation, L["StartSource" + i.SourceType] }
            .Any(v => v?.Contains(search, StringComparison.CurrentCultureIgnoreCase) == true));
        items = filter switch
        {
            1 => items.Where(i => i.Status == StartupStatus.Enabled),
            2 => items.Where(i => i.Status is StartupStatus.DisabledBySystemCore or StartupStatus.DisabledByWindowsOrSource),
            3 => items.Where(i => i.Scope == StartupScope.CurrentUser), 4 => items.Where(i => i.Scope == StartupScope.AllUsers),
            5 => items.Where(i => i.SourceType is StartupSourceType.RegistryRun or StartupSourceType.RegistryRunOnce),
            6 => items.Where(i => i.SourceType is StartupSourceType.StartupFolderUser or StartupSourceType.StartupFolderCommon),
            7 => items.Where(i => i.SourceType is StartupSourceType.ScheduledTaskLogon or StartupSourceType.ScheduledTaskBoot),
            _ => items
        };
        return sort switch { 1 => items.OrderBy(i => i.Publisher, StringComparer.CurrentCultureIgnoreCase), 2 => items.OrderBy(i => i.Status),
            3 => items.OrderBy(i => i.SourceType), 4 => items.OrderBy(i => i.Scope), _ => items.OrderBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase) };
    }
    public IReadOnlyList<StartupItem> Rows => Filtered().Skip(page * 20).Take(20).ToArray();
    public int Count => Filtered().Count();
    public string PageText => $"{page + 1} / {Math.Max(1, (Count + 19) / 20)} — {Count}";
    public bool HasNext => !busy && (page + 1) * 20 < Count;
    public bool HasPrevious => !busy && page > 0;
    public void MovePage(int offset) { page = Math.Clamp(page + offset, 0, Math.Max(0, (Count - 1) / 20)); selected = ""; Notify(); }
    public string Label(StartupItem item) => $"{item.Name} — {item.Publisher ?? L["StartUnknown"]}\n{L["StartStatus" + item.Status]} | {L["StartSource" + item.SourceType]} | {L["StartScope" + item.Scope]}";
    public string Details => Selected is { } item ? Label(item) + "\n" + L["StartImpact"] + ": " + L["StartNotMeasured"]
        + "\n" + L["StartProtection"] + ": " + L["StartProtection" + item.ProtectionReason]
        + "\n" + L["StartElevation"] + ": " + L[item.RequiresElevation ? "StartYes" : "StartNo"]
        + (item.SourceType == StartupSourceType.RegistryRunOnce ? "\n" + L["StartRunOnceWarning"] : "") : "";
    public string TechnicalDetails => Selected is { } item ? $"{item.SourceLocation}\n{item.ExecutablePath ?? L["StartUnknown"]}\n{item.ArgumentsDisplay}\n{item.CommandDisplay}\n{item.TriggerDescription}" : "";
    private async Task LoadAsync(bool force)
    {
        // A disposed view model must never start new provider work. Checked before any
        // lifetime token is read, because CancellationTokenSource.Token throws once disposed.
        if (disposed) return;
        busy = true; selected = "";
        operation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); Notify();
        try
        {
            if (session is null) { cachedSummary = await Task.Run(() => orchestrator.CacheStartupSummary(), operation.Token); Notify(); }
            session = await ReadAsync(force, operation.Token); inventoryActionable = true; page = 0; message = session.Inventory.Complete ? "StartLoaded" : "StartPartial";
        }
        // The UI cancel button cancels the active operation, not the lifetime. A genuine user
        // cancel must be classified as cancellation, never as a provider failure.
        catch (OperationCanceledException) when (disposed || lifetime.IsCancellationRequested || operation?.IsCancellationRequested == true) { inventoryActionable = false; message = "StartCancelled"; }
        // Expected only when shutdown began or the view model or a service it uses is
        // genuinely disposed. Anything else is a real fault and is recorded.
        catch (ObjectDisposedException) when (disposed || lifetime.IsCancellationRequested || orchestrator.IsShutdown) { inventoryActionable = false; }
        // Retention rows must never authorize a mutation after a failed read.
        catch (Exception e) { inventoryActionable = false; message = "StartLoadFailed"; diagnostics.Record(DiagnosticFeature.Startup, DiagnosticEvent.ProviderFailure, e.HResult); }
        finally { operation.Dispose(); operation = null; busy = false; Notify(); }
    }
    public void Cancel() => operation?.Cancel();
    // Surface a failed "Open file location" instead of silently doing nothing.
    public void ReportOpenLocation(bool opened) { if (!opened) { message = "StartOpenLocationFailed"; Notify(); } }
    public async Task ChangeAsync(StartupAction action, string confirmedId)
    {
        if (session is null || confirmedId != SelectedId || !(action == StartupAction.Disable ? CanDisable : CanEnable)) return;
        var request = new StartupOperationRequest(session.Id, selected, action, true);
        busy = changing = true; operation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); Notify();
        try
        {
            // Mutation consumes the service session even if it throws/cancels.
            // Keep its display, but expire the shared read value before use.
            inventoryActionable = false;
            orchestrator.Publish(DataDataset.Startup, session, TimeSpan.Zero);
            var result = await mutation.ApplyAsync(request, operation.Token);
            selected = "";
            // A successful mutation and a failed subsequent read are different outcomes.
            // Preserve the display, but do not misreport a read failure as mutation recovery.
            try { session = await ReadAsync(true, lifetime.Token); inventoryActionable = true; page = 0; }
            catch (ObjectDisposedException) when (disposed || orchestrator.IsShutdown) { message = "StartLoadFailed"; return; }
            // The mutation already succeeded, so this failure is still a real fault worth recording.
            catch (Exception e) { message = "StartLoadFailed"; diagnostics.Record(DiagnosticFeature.Startup, DiagnosticEvent.ProviderFailure, e.HResult); return; }
            message = "StartOutcome" + result.Outcome;
        }
        catch (OperationCanceledException) { message = "StartOutcomeRecoveryRequired"; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or System.Runtime.InteropServices.COMException) { message = "StartOutcomeRecoveryRequired"; }
        finally { operation.Dispose(); operation = null; busy = changing = false; Notify(); }
    }
    private void Notify() => OnPropertyChanged(string.Empty);
    public override void Dispose() { disposed = true; lifetime.Cancel(); session = null; base.Dispose(); }
}
