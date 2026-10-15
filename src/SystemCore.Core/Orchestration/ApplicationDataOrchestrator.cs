using System.Diagnostics;
using SystemCore.Core.Diagnostics;
using SystemCore.Core.Performance;

namespace SystemCore.Core.Orchestration;

// Producers are trusted application code. This layer has no mutation-service dependency.
public sealed partial class ApplicationDataOrchestrator(IPerformanceMonitorService monitor, IAppDiagnostics diagnostics, string cacheRootDirectory) : IApplicationDataOrchestrator
{
    public const int MaxConcurrentHeavyProviders = 2;
    public static readonly TimeSpan CleanerStaleThreshold = TimeSpan.FromMinutes(60);
    private static readonly TimeSpan ShutdownWait = TimeSpan.FromMilliseconds(500);
    private readonly object sync = new();
    private readonly object telemetryConsumer = new();
    private readonly CancellationTokenSource lifetime = new();
    private readonly SemaphoreSlim heavy = new(MaxConcurrentHeavyProviders, MaxConcurrentHeavyProviders);
    private readonly SnapshotCacheStore cache = new(cacheRootDirectory);
    private readonly Dictionary<DataDataset, Entry> entries = [];
    private readonly Dictionary<DataDataset, Warmup> warmups = [];
    // A refresh mark carries the ticket of the call that owns it, so a finishing call can
    // never clear a mark that a later call has already taken over for the same dataset.
    private readonly Dictionary<DataDataset, long> refreshing = [];
    private long refreshTicket;
    private readonly AsyncLocal<Flight?> warmupContext = new();
    private readonly Stopwatch watch = Stopwatch.StartNew();
    private StartupMeasurements measurements = new(DateTimeOffset.UtcNow, null, null, null, null, null, null, null, null, null);
    private bool started, startupReady, telemetryActive;
    // Volatile so IsShutdown can be observed from an exception filter. Disposal writes it once
    // under the lock; every reader must be able to see that write without taking the lock itself,
    // because a filter runs on whichever thread the failure landed on and may already hold one.
    private volatile bool disposed;
    private Task? startup, disposal;
    public event EventHandler<DatasetStatus>? StatusChanged;
    public StartupMeasurements Measurements { get { lock (sync) return measurements; } }

    private sealed class Entry
    {
        public Type? Type;
        public object? Value;
        public bool HasValue, Failed;
        public DateTimeOffset? WrittenAt;
        public TimeSpan? Lifetime;
        public long Revision;
        public Flight? Flight;
    }
    private sealed class Flight(bool warmup, LoadTier tier)
    {
        public readonly TaskCompletionSource<Result> Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly bool Warmup = warmup;
        public readonly LoadTier Tier = tier;
        public Task<Result>? Nested;
    }
    private sealed record Result(bool HasValue, object? Value);
    private sealed record Warmup(LoadTier Tier, Func<CancellationToken, Task> Producer, TimeSpan StaleAfter);

    public Task StartAsync()
    {
        try
        {
            lock (sync)
            {
                if (started || disposed) return Task.CompletedTask;
                started = true;
                startup = Task.Run(() =>
                {
                    try
                    {
                        StartLiveTelemetry();
                        // Tier 1 warms only display summaries. It never seeds a live provider/session value.
                        WarmCachedSummaries();
                        lock (sync)
                        {
                            if (disposed) return;
                            startupReady = true;
                            foreach (var (dataset, registration) in warmups.OrderBy(p => p.Value.Tier)) ScheduleWarmup(dataset, registration);
                        }
                    }
                    catch { } // Best-effort startup warmup; a producer fault must not escape the fire-and-forget task.
                });
            }
        }
        catch { } // Start never throws to its caller; per-dataset startup faults surface through the status stream.
        return Task.CompletedTask;
    }

    public void RegisterBackgroundWarmup(DataDataset dataset, LoadTier tier, Func<CancellationToken, Task> warmup, TimeSpan staleAfter)
    {
        ArgumentNullException.ThrowIfNull(warmup);
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var registration = new Warmup(tier, warmup, staleAfter);
            warmups[dataset] = registration;
            // Supports lazily constructed pages without requiring a second startup entry point.
            if (startupReady) ScheduleWarmup(dataset, registration);
        }
    }

    public Task<T> GetAsync<T>(DataDataset dataset, LoadTier tier, TimeSpan freshness, Func<CancellationToken, Task<T>> producer, CancellationToken token) =>
        Request(dataset, tier, freshness, producer, token, false);
    public Task<T> RefreshAsync<T>(DataDataset dataset, LoadTier tier, Func<CancellationToken, Task<T>> producer, CancellationToken token) =>
        Request(dataset, tier, TimeSpan.MaxValue, producer, token, true);

    private Task<T> Request<T>(DataDataset dataset, LoadTier tier, TimeSpan freshness, Func<CancellationToken, Task<T>> producer, CancellationToken token, bool force)
    {
        ArgumentNullException.ThrowIfNull(producer);
        token.ThrowIfCancellationRequested();
        Task<Result>? task = null; DatasetStatus? status = null; T? cached = default;
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var entry = EntryFor(dataset); BindType<T>(dataset, entry);
            if (entry.Flight is { } existing)
            {
                if (existing.Warmup && ReferenceEquals(warmupContext.Value, existing))
                {
                    // A registered page initializer can use the same GetAsync API without deadlocking itself.
                    existing.Nested ??= Task.Run(() => Produce(dataset, entry, existing, tier, producer));
                    task = existing.Nested;
                }
                else task = existing.Completion.Task;
            }
            else if (!force && entry.HasValue && !Stale(entry, freshness))
            {
                cached = (T)entry.Value!;
                status = new(dataset, DatasetState.Ready, entry.WrittenAt, null, true, false);
            }
            else
            {
                var flight = new Flight(false, tier); entry.Flight = flight;
                task = flight.Completion.Task;
                _ = Task.Run(() => RunFlight(dataset, entry, flight, () => Produce(dataset, entry, flight, tier, producer)));
            }
        }
        if (status is not null) { Notify(status); return Task.FromResult(cached!); }
        return AwaitValue(task!, dataset, tier, freshness, producer, token, force);
    }

    private async Task<T> AwaitValue<T>(Task<Result> task, DataDataset dataset, LoadTier tier, TimeSpan freshness,
        Func<CancellationToken, Task<T>> producer, CancellationToken token, bool force)
    {
        var result = await task.WaitAsync(token).ConfigureAwait(false);
        if (result.HasValue) return (T)result.Value!;
        // A Task-only warmup may not supply a value. Never invent a default inventory or actionable session.
        return await Request(dataset, tier, freshness, producer, token, force).ConfigureAwait(false);
    }

    private async Task<Result> Produce<T>(DataDataset dataset, Entry entry, Flight flight, LoadTier tier, Func<CancellationToken, Task<T>> producer)
    {
        var needsPermit = tier == LoadTier.Expensive && !(flight.Warmup && flight.Tier == LoadTier.Expensive);
        if (needsPermit) await heavy.WaitAsync(lifetime.Token).ConfigureAwait(false);
        try
        {
            long revision;
            lock (sync) { lifetime.Token.ThrowIfCancellationRequested(); revision = entry.Revision; RecordStart(dataset); }
            var value = await producer(lifetime.Token).ConfigureAwait(false);
            DatasetStatus? status = null;
            lock (sync)
            {
                lifetime.Token.ThrowIfCancellationRequested();
                if (revision == entry.Revision)
                {
                    entry.Value = value; entry.HasValue = true; entry.WrittenAt = DateTimeOffset.UtcNow;
                    entry.Lifetime = null; entry.Failed = false;
                    RecordReady(dataset); status = new(dataset, DatasetState.Ready, entry.WrittenAt, null, false, false);
                }
                value = (T)entry.Value!;
            }
            if (status is not null) Notify(status);
            return new(true, value);
        }
        finally { if (needsPermit) heavy.Release(); }
    }

    public bool IsShutdown => disposed;

    private void Release(DataDataset dataset, long ticket)
    {
        lock (sync) { if (refreshing.TryGetValue(dataset, out var held) && held == ticket) refreshing.Remove(dataset); }
    }

    public Task<int> RefreshStaleAsync(IReadOnlyList<DatasetRefreshRequest> requests, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(requests);
        List<(DataDataset Dataset, long Ticket, Func<CancellationToken, Task> Refresh)> due = [];
        lock (sync)
        {
            // Shutdown is decided here, under the same lock that Dispose uses, so a refresh
            // can never be admitted between the caller's check and the orchestrator's own.
            if (disposed || token.IsCancellationRequested) return Task.FromResult(0);
            foreach (var request in requests)
            {
                if (request.Refresh is null) continue;
                if (refreshing.ContainsKey(request.Dataset)) continue;
                if (!Stale(EntryFor(request.Dataset), request.StaleAfter)) continue;
                var ticket = ++refreshTicket;
                refreshing[request.Dataset] = ticket;
                due.Add((request.Dataset, ticket, request.Refresh));
            }
        }
        if (due.Count == 0) return Task.FromResult(0);
        _ = Task.Run(async () =>
        {
            try
            {
                foreach (var (dataset, ticket, refresh) in due)
                {
                    // Admission and execution are separated in time, and shutdown can begin in
                    // between. Re-checked per item, not once, so a refresh never starts late.
                    if (token.IsCancellationRequested || lifetime.IsCancellationRequested) return;
                    try { await refresh(lifetime.Token).ConfigureAwait(false); }
                    // Expected only on genuine cancellation. Both the caller's token and our own
                    // lifetime count, because a caller-side cancel tears this pass down.
                    catch (OperationCanceledException) when (lifetime.IsCancellationRequested || token.IsCancellationRequested) { }
                    // Expected only when teardown is genuinely under way. A read is refused with
                    // ObjectDisposedException exactly when the volatile disposed flag is set (see
                    // Request), and DisposeAsync sets that flag before it cancels the lifetime on
                    // a separate thread-pool work item. So teardown must be recognised from the
                    // disposed flag itself: keying only off lifetime cancellation left a window
                    // where disposed was true but the token was not yet cancelled, and a genuine
                    // shutdown read was then misrecorded as a provider failure.
                    catch (ObjectDisposedException) when (disposed || lifetime.IsCancellationRequested) { }
                    // A caller-supplied delegate that fails for any other reason is a real defect.
                    // It is recorded rather than swallowed, and never rendered to the user.
                    catch (Exception e) { diagnostics.Record(DiagnosticFeature.App, DiagnosticEvent.ProviderFailure, e.HResult); }
                    // Released as soon as this dataset is done, so one stalled dataset cannot
                    // hold the other three marked for the rest of the pass.
                    finally { Release(dataset, ticket); }
                }
            }
            finally
            {
                // Releases the marks of items that were admitted but never reached, for example
                // when the caller's token was cancelled mid-pass. Ticket-matched, so a dataset
                // another call has since re-admitted is left alone.
                foreach (var (dataset, ticket, _) in due) Release(dataset, ticket);
            }
        });
        return Task.FromResult(due.Count);
    }

    private void ScheduleWarmup(DataDataset dataset, Warmup registration)
    {
        var entry = EntryFor(dataset);
        if (entry.Flight is not null || !Stale(entry, registration.StaleAfter)) return;
        var flight = new Flight(true, registration.Tier); entry.Flight = flight;
        _ = Task.Run(() => RunFlight(dataset, entry, flight, async () =>
        {
            var permit = registration.Tier == LoadTier.Expensive;
            if (permit) await heavy.WaitAsync(lifetime.Token).ConfigureAwait(false);
            warmupContext.Value = flight;
            try
            {
                long revision;
                lock (sync) { lifetime.Token.ThrowIfCancellationRequested(); revision = entry.Revision; RecordStart(dataset); }
                await registration.Producer(lifetime.Token).ConfigureAwait(false);
                if (flight.Nested is { } nested) await nested.ConfigureAwait(false);
                Result result; DatasetStatus status;
                lock (sync)
                {
                    lifetime.Token.ThrowIfCancellationRequested();
                    if (flight.Nested is null && entry.Revision == revision)
                    {
                        // A void callback did not replace the typed value. Never present the previous value as its result.
                        entry.Value = null; entry.HasValue = false; entry.WrittenAt = DateTimeOffset.UtcNow; entry.Lifetime = null;
                    }
                    entry.Failed = false;
                    RecordReady(dataset); result = new(entry.HasValue, entry.Value);
                    status = new(dataset, DatasetState.Ready, entry.WrittenAt, null, false, false);
                }
                Notify(status); return result;
            }
            finally { warmupContext.Value = null; if (permit) heavy.Release(); }
        }));
    }

    private async Task RunFlight(DataDataset dataset, Entry entry, Flight flight, Func<Task<Result>> run)
    {
        Notify(new(dataset, DatasetState.Loading, entry.WrittenAt, null, false, true));
        Result? result = null; Exception? failure = null;
        try { result = await run().ConfigureAwait(false); }
        catch (Exception ex)
        {
            failure = ex;
            lock (sync) entry.Failed = true;
        }
        if (failure is not null)
            Notify(new(dataset, failure is OperationCanceledException && lifetime.IsCancellationRequested ? DatasetState.Empty : DatasetState.Failed,
                null, failure is OperationCanceledException && lifetime.IsCancellationRequested ? null : $"Refresh failed (0x{failure.HResult:X8}).", false, true));
        lock (sync)
        {
            if (ReferenceEquals(entry.Flight, flight)) entry.Flight = null;
            if (failure is null) flight.Completion.TrySetResult(result!);
            else if (failure is OperationCanceledException && lifetime.IsCancellationRequested) flight.Completion.TrySetCanceled(lifetime.Token);
            else flight.Completion.TrySetException(failure);
            // Startup has no awaiting caller. Observe faults here without hiding them from explicit callers.
            if (flight.Completion.Task.IsFaulted) _ = flight.Completion.Task.Exception;
        }
    }

    public bool IsStale(DataDataset dataset, TimeSpan freshness)
    {
        lock (sync)
        {
            if (entries.TryGetValue(dataset, out var entry) && (entry.WrittenAt.HasValue || entry.Failed)) return Stale(entry, freshness);
            if (!summaryTimes.TryGetValue(dataset, out var written)) return true;
            var age = DateTimeOffset.UtcNow - written;
            return age < TimeSpan.Zero || age >= freshness;
        }
    }
    private static bool Stale(Entry entry, TimeSpan freshness)
    {
        if (entry.Failed || entry.WrittenAt is not { } written) return true;
        var age = DateTimeOffset.UtcNow - written;
        return age < TimeSpan.Zero || freshness <= TimeSpan.Zero || age >= freshness || (entry.Lifetime is { } duration && age >= duration);
    }
    public void Publish<T>(DataDataset dataset, T value, TimeSpan lifetime)
    {
        DatasetStatus status;
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var entry = EntryFor(dataset); BindType<T>(dataset, entry);
            entry.Value = value; entry.HasValue = true; entry.WrittenAt = DateTimeOffset.UtcNow;
            entry.Lifetime = lifetime; entry.Failed = false; entry.Revision++;
            RecordReady(dataset); status = new(dataset, DatasetState.Ready, entry.WrittenAt, null, false, lifetime <= TimeSpan.Zero);
        }
        Notify(status);
    }
    private Entry EntryFor(DataDataset dataset)
    {
        if (!entries.TryGetValue(dataset, out var entry)) entries.Add(dataset, entry = new());
        return entry;
    }
    private static void BindType<T>(DataDataset dataset, Entry entry)
    {
        if (entry.Type is { } type && type != typeof(T)) throw new InvalidOperationException($"Dataset {dataset} requires a consistent value type ({type.Name}, not {typeof(T).Name}).");
        entry.Type = typeof(T);
    }
    private void Notify(DatasetStatus status)
    {
        var subscribers = StatusChanged;
        if (subscribers is null) return;
        foreach (EventHandler<DatasetStatus> subscriber in subscribers.GetInvocationList())
            try { subscriber(this, status); } catch { } // One faulty subscriber must not stop the remaining notifications.
    }

    public void StartLiveTelemetry()
    {
        lock (sync)
        {
            if (disposed || telemetryActive) return;
            monitor.Updated += OnTelemetry;
            try { monitor.SetActive(telemetryConsumer, true, detailed: true); telemetryActive = true; }
            catch { monitor.Updated -= OnTelemetry; }
        }
    }
    private void OnTelemetry(object? sender, PerformanceSnapshot snapshot)
    {
        lock (sync)
            if (!disposed && measurements.ToFirstDashboardMetrics is null &&
                (snapshot.Basic.CpuPercent.Value.HasValue || snapshot.Basic.Memory.Value is not null))
                measurements = measurements with { ToFirstDashboardMetrics = watch.Elapsed };
    }
    private void RecordStart(DataDataset dataset)
    {
        if (dataset == DataDataset.Cleaner && measurements.ToCleanerScanStart is null)
            measurements = measurements with { ToCleanerScanStart = watch.Elapsed };
    }
    private void RecordReady(DataDataset dataset)
    {
        var elapsed = watch.Elapsed;
        measurements = dataset switch
        {
            DataDataset.InstalledApplications when measurements.ToAppsInventoryReady is null => measurements with { ToAppsInventoryReady = elapsed },
            DataDataset.Games when measurements.ToGamesInventoryReady is null => measurements with { ToGamesInventoryReady = elapsed },
            DataDataset.Startup when measurements.ToStartupInventoryReady is null => measurements with { ToStartupInventoryReady = elapsed },
            DataDataset.SystemInformation when measurements.ToSystemInfoReady is null => measurements with { ToSystemInfoReady = elapsed },
            DataDataset.Cleaner when measurements.ToCleanerScanComplete is null => measurements with { ToCleanerScanComplete = elapsed },
            _ => measurements
        };
    }
    private void WarmCachedSummaries()
    {
        CacheDashboardSummary(); CacheSystemInformationSummary(); CacheApplicationsSummary(); CacheStartupSummary(); CacheCleanerSummary();
        foreach (var (dataset, written) in summaryTimes)
        {
            lock (sync)
            {
                if (disposed) return;
                if (entries.TryGetValue(dataset, out var entry) && (entry.Flight is not null || entry.WrittenAt.HasValue)) continue;
            }
            var freshness = dataset == DataDataset.Cleaner ? CleanerStaleThreshold : dataset == DataDataset.SystemInformation ? TimeSpan.FromDays(1) : TimeSpan.FromMinutes(15);
            Notify(new(dataset, DatasetState.Ready, written, null, true, DateTimeOffset.UtcNow - written >= freshness));
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (sync)
        {
            if (disposal is not null) return new(disposal);
            disposed = true;
            if (telemetryActive)
            {
                try { monitor.SetActive(telemetryConsumer, false); } catch { } // Best-effort telemetry teardown during dispose.
                monitor.Updated -= OnTelemetry; telemetryActive = false;
            }
            var tasks = entries.Values.Where(e => e.Flight is not null).Select(e => e.Flight!.Completion.Task).Cast<Task>().ToList();
            if (startup is not null) tasks.Add(startup);
            // A producer's cancellation callback must not block the UI shutdown thread either.
            tasks.Add(Task.Run(() => { try { lifetime.Cancel(); } catch { } })); // A producer's cancel callback must not fault shutdown.
            disposal = Settle(tasks);
            return new(disposal);
        }
    }
    private async Task Settle(List<Task> tasks)
    {
        var all = Task.WhenAll(tasks);
        try { await all.WaitAsync(ShutdownWait).ConfigureAwait(false); } catch { } // Shutdown is best-effort; in-flight faults are observed on `all` below.
        _ = all.ContinueWith(completed =>
        {
            _ = completed.Exception;
            lifetime.Dispose(); heavy.Dispose();
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }
}
