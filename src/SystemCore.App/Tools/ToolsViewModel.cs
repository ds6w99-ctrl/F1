using SystemCore.App.Services;
using SystemCore.App.ViewModels;
using SystemCore.Core.Diagnostics;
using SystemCore.Core.Tools;
using SystemCore.Core.Orchestration;

namespace SystemCore.App.Tools;

public sealed class ToolsViewModel : LocalizedViewModel
{
    // Installed Windows tools change very rarely.
    public static readonly TimeSpan ToolsFreshness = TimeSpan.FromMinutes(30);
    private readonly IWindowsToolsService tools;
    private readonly IAppDiagnostics diagnostics;
    private readonly IApplicationDataOrchestrator orchestrator;
    private Task? load;
    public ToolsViewModel(LocalizationService localizer, IWindowsToolsService tools, IAppDiagnostics diagnostics,
        IApplicationDataOrchestrator orchestrator) : base(localizer)
    {
        this.tools = tools; this.diagnostics = diagnostics; this.orchestrator = orchestrator;
        orchestrator.RegisterBackgroundWarmup(DataDataset.Tools, LoadTier.Background,
            async token => await ReadAsync(false, token), ToolsFreshness);
    }
    private readonly CancellationTokenSource lifetime = new();
    private bool busy;
    private bool disposed;
    private WindowsToolId? lastTool;
    private ToolLaunchResult? result;
    private string status = "ToolsLoading";
    public IReadOnlyList<WindowsToolItem> Items { get; private set; } = [];
    public string Text(string key) => L[key];
    public string Title => L["NavTools"];
    public bool Busy => busy;
    public string Status => result is { } outcome && lastTool is { } tool ? L.Format("ToolResult", L["Tool" + tool], L["ToolResult" + outcome]) : L[status];
    private Task<IReadOnlyList<WindowsToolItem>> ReadAsync(bool force, CancellationToken token) => force
        ? orchestrator.RefreshAsync(DataDataset.Tools, LoadTier.Background, tools.ReadAsync, token)
        : orchestrator.GetAsync(DataDataset.Tools, LoadTier.Background, ToolsFreshness, tools.ReadAsync, token);
    public Task RefreshIfStaleAsync() => orchestrator.IsStale(DataDataset.Tools, ToolsFreshness) ? InitializeAsync() : Task.CompletedTask;
    public Task InitializeAsync() => StartLoad(false);
    public Task RefreshAsync() => StartLoad(true);
    private Task StartLoad(bool force) => disposed ? Task.CompletedTask
        : load is { IsCompleted: false } ? load : busy ? Task.CompletedTask : load = LoadAsync(force);
    private async Task LoadAsync(bool force)
    {
        // A disposed view model must never start new provider work. Checked before the
        // lifetime token is read, because CancellationTokenSource.Token throws once disposed.
        if (disposed) return;
        busy = true; result = null; status = "ToolsLoading"; OnPropertyChanged(string.Empty);
        try { Items = await ReadAsync(force, lifetime.Token); status = "ToolsReady"; }
        catch (OperationCanceledException) when (disposed || lifetime.IsCancellationRequested) { }
        // Expected only when shutdown began or the view model or a service it uses is
        // genuinely disposed. Anything else is a real fault and is recorded.
        catch (ObjectDisposedException) when (disposed || lifetime.IsCancellationRequested || orchestrator.IsShutdown) { }
        catch (Exception e) { status = "ToolsLoadFailed"; diagnostics.Record(DiagnosticFeature.Tools, DiagnosticEvent.ProviderFailure, e.HResult); }
        finally { busy = false; OnPropertyChanged(string.Empty); }
    }
    public async Task OpenAsync(WindowsToolId id)
    {
        if (busy) return; busy = true; result = null; status = "ToolsOpening"; OnPropertyChanged(string.Empty);
        try { lastTool = id; result = await tools.OpenAsync(id, lifetime.Token); diagnostics.Record(DiagnosticFeature.Tools, DiagnosticEvent.ToolLaunch, (int)id * 10 + (int)result); }
        catch (OperationCanceledException) { result = ToolLaunchResult.Cancelled; }
        catch (Exception e) when (e is not OutOfMemoryException) { result = ToolLaunchResult.Failed; diagnostics.Record(DiagnosticFeature.Tools, DiagnosticEvent.ProviderFailure, e.HResult); }
        finally { busy = false; OnPropertyChanged(string.Empty); }
    }
    public override void Dispose() { disposed = true; lifetime.Cancel(); base.Dispose(); }
}
