namespace SystemCore.Core.Orchestration;

public enum DataDataset { Dashboard, SystemInformation, InstalledApplications, Games, Startup, Tools, Cleaner, Performance, Memory }
public enum LoadTier { Cached, Background, Expensive }
public enum DatasetState { Empty, Loading, Ready, Failed }
public sealed record DatasetStatus(DataDataset Dataset, DatasetState DatasetState, DateTimeOffset? LoadedAt, string? Error, bool FromCache, bool Stale);
public sealed record StartupMeasurements(
    DateTimeOffset StartedAt, TimeSpan? ToWindowVisible, TimeSpan? ToFirstDashboardMetrics,
    TimeSpan? ToUsableNavigation, TimeSpan? ToAppsInventoryReady, TimeSpan? ToGamesInventoryReady,
    TimeSpan? ToStartupInventoryReady, TimeSpan? ToSystemInfoReady,
    TimeSpan? ToCleanerScanStart, TimeSpan? ToCleanerScanComplete);

public sealed record DashboardSummarySnapshot(int InstalledApplications, int StartupItems, long CleanerBytes, DateTimeOffset CapturedAt);
public sealed record SystemInformationSummarySnapshot(int LogicalProcessors, ulong PhysicalMemoryBytes, int StorageDevices, DateTimeOffset CapturedAt);
public sealed record ApplicationsSummarySnapshot(int Applications, int Games, bool Complete, DateTimeOffset CapturedAt);
public sealed record StartupSummarySnapshot(int Total, int Enabled, int Disabled, int Protected, bool Complete, DateTimeOffset CapturedAt);
public sealed record CleanerSummarySnapshot(bool Complete, long Bytes, long Items, DateTimeOffset CapturedAt);

// One caller-supplied refresh action for a dataset, evaluated by the orchestrator
// so that staleness, in-flight de-duplication and the lifetime token stay in one place.
public sealed record DatasetRefreshRequest(DataDataset Dataset, TimeSpan StaleAfter, Func<CancellationToken, Task> Refresh);

public interface IApplicationDataOrchestrator : IAsyncDisposable
{
    Task StartAsync();
    Task<T> GetAsync<T>(DataDataset dataset, LoadTier tier, TimeSpan freshness,
        Func<CancellationToken, Task<T>> producer, CancellationToken token);
    Task<T> RefreshAsync<T>(DataDataset dataset, LoadTier tier,
        Func<CancellationToken, Task<T>> producer, CancellationToken token);
    bool IsStale(DataDataset dataset, TimeSpan freshness);

    // True once the orchestrator has been disposed. Callers use it to tell an expected
    // teardown ObjectDisposedException from an unexpected one.
    bool IsShutdown { get; }

    // Starts at most one refresh per dataset, and only for datasets that are stale.
    // Never throws, never starts a second run while the first is in flight, and never
    // runs after shutdown. Returns how many datasets this call actually started.
    Task<int> RefreshStaleAsync(IReadOnlyList<DatasetRefreshRequest> requests, CancellationToken token = default);
    void Publish<T>(DataDataset dataset, T value, TimeSpan lifetime);
    event EventHandler<DatasetStatus>? StatusChanged;
    void StartLiveTelemetry();
    StartupMeasurements Measurements { get; }
    void RegisterBackgroundWarmup(DataDataset dataset, LoadTier tier,
        Func<CancellationToken, Task> warmup, TimeSpan staleAfter);

    // Null means read; a value means persist a display-only summary, never an actionable session.
    DashboardSummarySnapshot? CacheDashboardSummary(DashboardSummarySnapshot? value = null, TimeSpan? maxAge = null);
    SystemInformationSummarySnapshot? CacheSystemInformationSummary(SystemInformationSummarySnapshot? value = null, TimeSpan? maxAge = null);
    ApplicationsSummarySnapshot? CacheApplicationsSummary(ApplicationsSummarySnapshot? value = null, TimeSpan? maxAge = null);
    StartupSummarySnapshot? CacheStartupSummary(StartupSummarySnapshot? value = null, TimeSpan? maxAge = null);
    CleanerSummarySnapshot? CacheCleanerSummary(CleanerSummarySnapshot? value = null, TimeSpan? maxAge = null);
}
