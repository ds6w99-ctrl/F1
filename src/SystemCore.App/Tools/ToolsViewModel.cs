using SystemCore.App.Services;
using SystemCore.App.ViewModels;
using SystemCore.Core.Diagnostics;
using SystemCore.Core.Tools;

namespace SystemCore.App.Tools;

public sealed class ToolsViewModel(LocalizationService localizer, IWindowsToolsService tools, IAppDiagnostics diagnostics) : LocalizedViewModel(localizer)
{
    private readonly CancellationTokenSource lifetime = new();
    private bool busy, loaded;
    private WindowsToolId? lastTool;
    private ToolLaunchResult? result;
    private string status = "ToolsReady";
    public IReadOnlyList<WindowsToolItem> Items { get; private set; } = [];
    public string Text(string key) => L[key];
    public string Title => L["NavTools"];
    public bool Busy => busy;
    public string Status => result is { } outcome && lastTool is { } tool ? L.Format("ToolResult", L["Tool" + tool], L["ToolResult" + outcome]) : L[status];
    public Task InitializeAsync() => loaded ? Task.CompletedTask : RefreshAsync();
    public async Task RefreshAsync()
    {
        if (busy) return; busy = true; result = null; status = "ToolsLoading"; OnPropertyChanged(string.Empty);
        try { Items = await tools.ReadAsync(lifetime.Token); loaded = true; status = "ToolsReady"; }
        catch (OperationCanceledException) { }
        catch (Exception e) when (e is not OutOfMemoryException) { status = "ToolsFailure"; diagnostics.Record(DiagnosticFeature.Tools, DiagnosticEvent.ProviderFailure, e.HResult); }
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
    public override void Dispose() { lifetime.Cancel(); base.Dispose(); }
}
