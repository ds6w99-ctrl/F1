// The console harness executes real view models, presentation and orchestration.
// Only WinUI's dispatch/visibility boundary is replaced; no provider/load logic lives here.
namespace Microsoft.UI.Xaml { public enum Visibility { Visible, Collapsed } }
namespace SystemCore.App.Services
{
    public sealed class UiDispatcher
    {
        public bool Post(Action action) { action(); return true; }
    }
}

internal static class InventoryTestOrchestrator
{
    public static SystemCore.Core.Orchestration.ApplicationDataOrchestrator Create(
        SystemCore.Core.Diagnostics.IAppDiagnostics? diagnostics = null) => new(new Monitor(), diagnostics ?? new NullDiagnostics(),
        Path.Combine(Path.GetTempPath(), "F1-inventory-tests", Guid.NewGuid().ToString("N")));
    internal sealed class NullDiagnostics : SystemCore.Core.Diagnostics.IAppDiagnostics
    {
        public void Record(SystemCore.Core.Diagnostics.DiagnosticFeature feature, SystemCore.Core.Diagnostics.DiagnosticEvent action, int code = 0) { }
        public Task FlushAsync(CancellationToken token = default) => Task.CompletedTask;
        public SystemCore.Core.Diagnostics.DiagnosticsStatus GetStatus() => new(true, 0, 0, 0);
        public Task<SystemCore.Core.Diagnostics.DiagnosticOperation> ExportAsync(CancellationToken token) => throw new NotSupportedException();
        public Task<SystemCore.Core.Diagnostics.DiagnosticOperation> ClearOldAsync(CancellationToken token) => throw new NotSupportedException();
        public Task<bool> OpenFolderAsync(SystemCore.Core.Diagnostics.DiagnosticFolder folder, CancellationToken token) => throw new NotSupportedException();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    internal sealed class Monitor : SystemCore.Core.Performance.IPerformanceMonitorService
    {
        public event EventHandler<SystemCore.Core.Performance.PerformanceSnapshot>? Updated { add { } remove { } }
        public SystemCore.Core.Performance.PerformanceSnapshot? Latest => null;
        public void SetActive(object consumer, bool active, bool detailed = false) { }
        public IReadOnlyList<SystemCore.Core.Performance.HistoryPoint> GetHistory() => [];
        public Task StopAsync() => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
