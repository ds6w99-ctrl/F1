namespace SystemCore.Core.Diagnostics;

public enum DiagnosticFeature { App, Dashboard, Cleaner, Uninstaller, Performance, SystemInfo, MemoryTools, Startup, Tools, Settings, About, Diagnostics }
public enum DiagnosticEvent { Started, Closed, Navigation, SettingsSaved, SettingsSaveFailed, ToolLaunch, SourceEvent, ProviderFailure, UnhandledFailure, Exported, LogsCleared }
public enum DiagnosticFolder { ApplicationData, Logs, Exports }
public sealed record ApplicationLogRecord(DateTimeOffset Timestamp, DiagnosticFeature Feature, DiagnosticEvent Event, int Code);
public sealed record DiagnosticsStatus(bool Available, int Files, long Bytes, long DroppedRecords);
public sealed record DiagnosticOperation(bool Succeeded, int Count = 0, string? FileName = null);
public interface IAppDiagnostics : IAsyncDisposable
{
    void Record(DiagnosticFeature feature, DiagnosticEvent action, int code = 0);
    Task FlushAsync(CancellationToken token = default);
    DiagnosticsStatus GetStatus();
    Task<DiagnosticOperation> ExportAsync(CancellationToken token);
    Task<DiagnosticOperation> ClearOldAsync(CancellationToken token);
    Task<bool> OpenFolderAsync(DiagnosticFolder folder, CancellationToken token);
}
