using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using SystemCore.Core.Dashboard;
using SystemCore.Core.MemoryTools;
using SystemCore.Core.Orchestration;
using SystemCore.Core.Performance;

internal static class OrchestrationTests
{
    public static async Task<bool> Run()
    {
        var passed = 0; var failed = 0;
        void Check(string name, bool result)
        {
            if (result) passed++; else failed++;
            Console.WriteLine($"{(result ? "PASS" : "FAIL")}: Orchestration: {name}");
        }
        var root = Path.Combine(AppContext.BaseDirectory, "orchestration-fixture-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var source = new FixtureSource();
            await using var monitor = new PerformanceMonitorService(source);
            await using var app = new ApplicationDataOrchestrator(monitor, root);
            var statuses = new List<DatasetStatus>();
            app.StatusChanged += (_, status) => { lock (statuses) statuses.Add(status); };
            Check("measurements start without fabricated timings", app.Measurements.StartedAt != default &&
                app.Measurements is { ToWindowVisible: null, ToUsableNavigation: null, ToFirstDashboardMetrics: null,
                    ToAppsInventoryReady: null, ToGamesInventoryReady: null, ToStartupInventoryReady: null,
                    ToSystemInfoReady: null, ToCleanerScanStart: null, ToCleanerScanComplete: null });
            Check("named policy values", ApplicationDataOrchestrator.MaxConcurrentHeavyProviders == 2 &&
                ApplicationDataOrchestrator.CleanerStaleThreshold == TimeSpan.FromMinutes(60));
            Check("absent is stale", app.IsStale(DataDataset.Games, TimeSpan.FromMinutes(1)));
            var started = Signal(); var release = Signal(); var calls = 0;
            async Task<int> Produce(CancellationToken token) { Interlocked.Increment(ref calls); started.TrySetResult(); await release.Task.WaitAsync(token); return 42; }
            var first = app.GetAsync(DataDataset.Games, LoadTier.Background, TimeSpan.FromMinutes(1), Produce, default);
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var rest = Enumerable.Range(0, 20).Select(_ => app.GetAsync(DataDataset.Games, LoadTier.Background, TimeSpan.FromMinutes(1), Produce, default)).ToArray();
            var mismatch = false;
            try { await app.GetAsync(DataDataset.Games, LoadTier.Background, TimeSpan.FromMinutes(1), _ => Task.FromResult("wrong"), default); }
            catch (InvalidOperationException) { mismatch = true; }
            Check("mismatched in-flight type refuses", mismatch && calls == 1);
            var refresh = app.RefreshAsync(DataDataset.Games, LoadTier.Background, Produce, default);
            release.TrySetResult();
            Check("concurrent calls and forced refresh share provider", (await Task.WhenAll(rest.Append(first).Append(refresh))).All(v => v == 42) && calls == 1);
            Check("fresh value bypasses provider", await app.GetAsync(DataDataset.Games, LoadTier.Background, TimeSpan.FromMinutes(1), Produce, default) == 42 && calls == 1);
            Check("freshness status identifies memory cache", statuses.Last(s => s.Dataset == DataDataset.Games).FromCache);
            Check("cheap freshness reads do not run provider", !app.IsStale(DataDataset.Games, TimeSpan.FromMinutes(1)) && calls == 1);
            await app.GetAsync(DataDataset.Games, LoadTier.Background, TimeSpan.Zero, Produce, default);
            Check("expired value reruns provider", calls == 2);
            await app.RefreshAsync(DataDataset.Games, LoadTier.Background, Produce, default);
            Check("manual refresh bypasses freshness", calls == 3);
            var waiterStarted = Signal(); var waiterRelease = Signal(); var sharedCalls = 0;
            using (var callerCancellation = new CancellationTokenSource())
            {
                var caller = app.RefreshAsync(DataDataset.Games, LoadTier.Background, async token =>
                { sharedCalls++; waiterStarted.TrySetResult(); await waiterRelease.Task.WaitAsync(token); return 43; }, callerCancellation.Token);
                await waiterStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
                var otherCaller = app.RefreshAsync(DataDataset.Games, LoadTier.Background, _ => { sharedCalls++; return Task.FromResult(44); }, default);
                callerCancellation.Cancel();
                try { await caller; } catch (OperationCanceledException) { }
                waiterRelease.TrySetResult();
                Check("one cancelled waiter cannot cancel shared work", caller.IsCanceled && await otherCaller == 43 && sharedCalls == 1);
            }
            var publishStarted = Signal(); var publishRelease = Signal();
            var older = app.RefreshAsync(DataDataset.Games, LoadTier.Background, async token =>
            { publishStarted.TrySetResult(); await publishRelease.Task.WaitAsync(token); return 1; }, default);
            await publishStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            app.Publish(DataDataset.Games, 99, TimeSpan.FromMinutes(1)); publishRelease.TrySetResult();
            Check("event publish wins over older in-flight snapshot", await older == 99);
            app.Publish(DataDataset.Startup, 7, TimeSpan.FromMinutes(1));
            Check("publish sets ready and fresh", !app.IsStale(DataDataset.Startup, TimeSpan.FromMinutes(2)) && statuses.Last(s => s.Dataset == DataDataset.Startup).DatasetState == DatasetState.Ready);
            app.Publish(DataDataset.Startup, 8, TimeSpan.Zero);
            Check("publish lifetime caps caller freshness", app.IsStale(DataDataset.Startup, TimeSpan.FromDays(1)));
            var failedOnce = false;
            try { await app.GetAsync<int>(DataDataset.Tools, LoadTier.Background, TimeSpan.FromMinutes(1), _ => throw new IOException(@"C:\private\command.exe --secret"), default); }
            catch (IOException) { failedOnce = true; }
            var failure = statuses.Last(s => s.Dataset == DataDataset.Tools);
            Check("failure sanitized and loaded time cleared", failedOnce && failure.DatasetState == DatasetState.Failed && failure.LoadedAt is null &&
                !string.IsNullOrWhiteSpace(failure.Error) && !failure.Error.Contains("private") && !failure.Error.Contains("secret"));
            Check("failure clears gate", await app.GetAsync(DataDataset.Tools, LoadTier.Background, TimeSpan.FromMinutes(1), _ => Task.FromResult(9), default) == 9);
            Check("inventory first-ready measured", app.Measurements.ToGamesInventoryReady.HasValue && app.Measurements.ToStartupInventoryReady.HasValue);

            var queued = Signal(); var heavyRelease = Signal(); var running = 0; var maximum = 0;
            async Task<int> Heavy(CancellationToken token)
            {
                var active = Interlocked.Increment(ref running);
                lock (queued) maximum = Math.Max(maximum, active);
                if (active == 2) queued.TrySetResult();
                try { await heavyRelease.Task.WaitAsync(token); return 1; }
                finally { Interlocked.Decrement(ref running); }
            }
            var heavy = new[] { DataDataset.Cleaner, DataDataset.Memory, DataDataset.Performance, DataDataset.SystemInformation }
                .Select(d => app.RefreshAsync(d, LoadTier.Expensive, Heavy, default)).ToArray();
            await queued.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Check("heavy providers queue at two", running == 2 && maximum == 2);
            var beforeConsumers = monitor.GetLifecycleDiagnostics().Consumers;
            await app.StartAsync().WaitAsync(TimeSpan.FromSeconds(1));
            Check("startup does not await expensive work", heavy.Any(t => !t.IsCompleted));
            app.StartLiveTelemetry(); app.StartLiveTelemetry();
            Check("one dedicated telemetry consumer", monitor.GetLifecycleDiagnostics().Consumers == beforeConsumers + 1);
            await source.Sampled.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Check("startup telemetry requests disk network and memory pressure", source.Detailed);
            heavyRelease.TrySetResult(); await Task.WhenAll(heavy);
            Check("heavy maximum remains bounded", maximum == 2);
            Check("cleaner scan timings recorded", app.Measurements.ToCleanerScanStart.HasValue && app.Measurements.ToCleanerScanComplete.HasValue);

            await using (var warm = new ApplicationDataOrchestrator(monitor, Path.Combine(root, "warm")))
            {
                var warmStarted = Signal(); var warmRelease = Signal(); var warmCalls = 0; var replaced = 0;
                warm.RegisterBackgroundWarmup(DataDataset.InstalledApplications, LoadTier.Expensive, _ => { replaced++; return Task.CompletedTask; }, TimeSpan.FromMinutes(1));
                warm.RegisterBackgroundWarmup(DataDataset.InstalledApplications, LoadTier.Expensive, async token =>
                {
                    await warm.GetAsync(DataDataset.InstalledApplications, LoadTier.Expensive, TimeSpan.FromMinutes(1), async inner =>
                    { Interlocked.Increment(ref warmCalls); warmStarted.TrySetResult(); await warmRelease.Task.WaitAsync(inner); return 15; }, token);
                }, TimeSpan.FromMinutes(1));
                await warm.StartAsync().WaitAsync(TimeSpan.FromSeconds(1));
                await warmStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
                var page = warm.RefreshAsync(DataDataset.InstalledApplications, LoadTier.Expensive, _ => { warmCalls++; return Task.FromResult(16); }, default);
                warmRelease.TrySetResult();
                Check("warmup and page share reentrant gate", await page.WaitAsync(TimeSpan.FromSeconds(5)) == 15 && warmCalls == 1 && replaced == 0);
                await warm.StartAsync();
                Check("startup idempotent", warmCalls == 1);
            }
            await using (var badWarm = new ApplicationDataOrchestrator(monitor, Path.Combine(root, "bad-warm")))
            {
                var failedWarm = new TaskCompletionSource<DatasetStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
                badWarm.StatusChanged += (_, s) => { if (s.DatasetState == DatasetState.Failed) failedWarm.TrySetResult(s); };
                badWarm.RegisterBackgroundWarmup(DataDataset.Tools, LoadTier.Background, _ => throw new InvalidOperationException("private"), TimeSpan.Zero);
                await badWarm.StartAsync().WaitAsync(TimeSpan.FromSeconds(1));
                Check("warmup fault cannot escape startup", (await failedWarm.Task.WaitAsync(TimeSpan.FromSeconds(5))).Error is { Length: > 0 });
                Check("warmup failure permits retry", await badWarm.GetAsync(DataDataset.Tools, LoadTier.Background, TimeSpan.Zero, _ => Task.FromResult(3), default) == 3);
            }
            await using (var freshWarm = new ApplicationDataOrchestrator(monitor, Path.Combine(root, "fresh-warm")))
            {
                var skippedCalls = 0; var sentinel = Signal();
                freshWarm.Publish(DataDataset.Tools, 5, TimeSpan.FromMinutes(1));
                freshWarm.RegisterBackgroundWarmup(DataDataset.Tools, LoadTier.Cached, _ => { skippedCalls++; return Task.CompletedTask; }, TimeSpan.FromMinutes(1));
                freshWarm.RegisterBackgroundWarmup(DataDataset.Startup, LoadTier.Background, _ => { sentinel.TrySetResult(); return Task.CompletedTask; }, TimeSpan.Zero);
                await freshWarm.StartAsync(); await sentinel.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Check("warmup respects ready value freshness", skippedCalls == 0);
            }
            await using (var voidWarm = new ApplicationDataOrchestrator(monitor, Path.Combine(root, "void-warm")))
            {
                voidWarm.Publish(DataDataset.Tools, 7, TimeSpan.Zero);
                var ready = Signal(); var finish = Signal();
                voidWarm.RegisterBackgroundWarmup(DataDataset.Tools, LoadTier.Background, async token => { ready.TrySetResult(); await finish.Task.WaitAsync(token); }, TimeSpan.Zero);
                await voidWarm.StartAsync(); await ready.Task.WaitAsync(TimeSpan.FromSeconds(5));
                var typedCalls = 0;
                var typedRequest = voidWarm.GetAsync(DataDataset.Tools, LoadTier.Background, TimeSpan.FromMinutes(1),
                    _ => { typedCalls++; return Task.FromResult(8); }, default);
                finish.TrySetResult();
                Check("void warmup cannot reauthorize a previous typed value", await typedRequest == 8 && typedCalls == 1);
            }
            var cancellationSeen = Signal(); var cancellationStarted = Signal();
            var cancelApp = new ApplicationDataOrchestrator(monitor, Path.Combine(root, "cancel"));
            DatasetStatus? cancelStatus = null;
            cancelApp.StatusChanged += (_, s) => cancelStatus = s;
            var cancelled = cancelApp.GetAsync<int>(DataDataset.Tools, LoadTier.Background, TimeSpan.Zero, async token =>
            {
                using var registration = token.Register(() => cancellationSeen.TrySetResult());
                cancellationStarted.TrySetResult(); await Task.Delay(Timeout.Infinite, token); return 0;
            }, default);
            await cancellationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await cancelApp.DisposeAsync();
            try { await cancelled; } catch (OperationCanceledException) { }
            Check("lifetime cancellation reaches provider without crash status", cancellationSeen.Task.IsCompleted && cancelled.IsCanceled && cancelStatus is { DatasetState: DatasetState.Empty, Error: null });
            var stuckApp = new ApplicationDataOrchestrator(monitor, Path.Combine(root, "stuck"));
            var stuckStarted = Signal(); var stuckRelease = Signal();
            var stuck = stuckApp.GetAsync(DataDataset.Tools, LoadTier.Expensive, TimeSpan.Zero, async _ => { stuckStarted.TrySetResult(); await stuckRelease.Task; return 1; }, default);
            await stuckStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var watch = Stopwatch.StartNew(); await stuckApp.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3));
            Check("uncooperative producer cannot hang disposal", watch.Elapsed < TimeSpan.FromSeconds(3));
            stuckRelease.TrySetResult(); try { await stuck; } catch (OperationCanceledException) { }
            await app.DisposeAsync();
            Check("telemetry consumer released without stopping shared monitor", monitor.GetLifecycleDiagnostics().Consumers == beforeConsumers && monitor.GetLifecycleDiagnostics().Workers == 1);

            await using var cacheApp = new ApplicationDataOrchestrator(monitor, Path.Combine(root, "cache"));
            var summary = new DashboardSummarySnapshot(12, 3, 1024, DateTimeOffset.UtcNow);
            Check("typed cache round trip", cacheApp.CacheDashboardSummary(summary) == summary && cacheApp.CacheDashboardSummary() == summary);
            Check("missing typed cache is miss", cacheApp.CacheStartupSummary() is null);
            cacheApp.CacheCleanerSummary(new(true, 99, 3, DateTimeOffset.UtcNow));
            Check("persisted cleaner summary supplies cheap freshness", !cacheApp.IsStale(DataDataset.Cleaner, ApplicationDataOrchestrator.CleanerStaleThreshold));
            var cachedReady = new TaskCompletionSource<DatasetStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
            cacheApp.StatusChanged += (_, s) => { if (s.Dataset == DataDataset.Dashboard && s.FromCache) cachedReady.TrySetResult(s); };
            await cacheApp.StartAsync();
            var warmed = await Task.WhenAny(cachedReady.Task, Task.Delay(1000));
            Check("startup exposes cached display summary status", warmed == cachedReady.Task && cachedReady.Task.Result.DatasetState == DatasetState.Ready);
            using (var locked = new FileStream(Path.Combine(root, "cache", "dashboard.json"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                Check("Tier 1 warms typed summary in memory", cacheApp.CacheDashboardSummary() == summary);
            var cacheType = typeof(ApplicationDataOrchestrator).GetNestedType("SnapshotCacheStore", BindingFlags.NonPublic)!;
            var cache = Activator.CreateInstance(cacheType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, [Path.Combine(root, "raw")], null)!;
            var write = cacheType.GetMethod("Write")!;
            var read = cacheType.GetMethod("TryRead")!.MakeGenericMethod(typeof(int));
            void Write<T>(string key, T value) => write.MakeGenericMethod(typeof(T)).Invoke(cache, [key, value]);
            bool Read(string key, out int value, TimeSpan? age = null)
            {
                object?[] args = [key, null, default(DateTimeOffset), age];
                var result = (bool)read.Invoke(cache, args)!; value = (int)args[1]!; return result;
            }
            Write("good", 7);
            Check("private store round trip", Read("good", out var raw) && raw == 7);
            Check("missing raw cache", !Read("missing", out _));
            var rawRoot = Path.Combine(root, "raw");
            File.WriteAllText(Path.Combine(rawRoot, "bad.json"), "{");
            Check("malformed JSON cache miss", !Read("bad", out _));
            File.WriteAllText(Path.Combine(rawRoot, "schema.json"), JsonSerializer.Serialize(new { SchemaVersion = 999, WrittenAt = DateTimeOffset.UtcNow, Value = 4 }));
            Check("schema mismatch cache miss", !Read("schema", out _));
            File.WriteAllText(Path.Combine(rawRoot, "missing-value.json"), JsonSerializer.Serialize(new { SchemaVersion = 1, WrittenAt = DateTimeOffset.UtcNow }));
            Check("incomplete envelope is not a zero-valued cache hit", !Read("missing-value", out _));
            File.WriteAllText(Path.Combine(rawRoot, "old.json"), JsonSerializer.Serialize(new { SchemaVersion = 1, WrittenAt = DateTimeOffset.UtcNow.AddDays(-2), Value = 4 }));
            Check("expired disk payload rejected", !Read("old", out _, TimeSpan.FromHours(1)));
            Check("expiry preserves last known good for explicit stale read", Read("old", out raw) && raw == 4);
            File.WriteAllText(Path.Combine(rawRoot, "future.json"), JsonSerializer.Serialize(new { SchemaVersion = 1, WrittenAt = DateTimeOffset.UtcNow.AddDays(2), Value = 4 }));
            Check("future write timestamp rejected", !Read("future", out _));
            Write("../outside", 5); Write("..\\outside", 5);
            Check("traversal cannot read or write outside root", !Read("../outside", out _) && !Read("..\\outside", out _) && !File.Exists(Path.Combine(root, "outside.json")));
            using (var locked = new FileStream(Path.Combine(rawRoot, "good.tmp"), FileMode.Create, FileAccess.ReadWrite, FileShare.None)) Write("good", 8);
            Check("failed temp write preserves previous payload", Read("good", out raw) && raw == 7);
            Write("good", new Unserializable());
            Check("serialization failure preserves previous payload", Read("good", out raw) && raw == 7);
            using (var locked = new FileStream(Path.Combine(rawRoot, "good.json"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                Check("locked cache read is miss", !Read("good", out _));
            var invalidCache = Activator.CreateInstance(cacheType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, ["\0"], null)!;
            write.MakeGenericMethod(typeof(int)).Invoke(invalidCache, ["good", 1]);
            Check("invalid cache root fails closed without throwing", !(bool)read.Invoke(invalidCache, ["good", null, default(DateTimeOffset), null])!);
            var surface = typeof(IApplicationDataOrchestrator).GetMethods();
            var allowed = new[] { "StartAsync", "GetAsync", "RefreshAsync", "IsStale", "Publish", "StartLiveTelemetry", "get_Measurements", "add_StatusChanged", "remove_StatusChanged", "RegisterBackgroundWarmup",
                "CacheDashboardSummary", "CacheSystemInformationSummary", "CacheApplicationsSummary", "CacheStartupSummary", "CacheCleanerSummary" };
            // This proves the orchestrator has no direct mutation capability. Trusted delegate bodies require separate call-graph review.
            Check("R1 own public surface excludes mutation entry points", surface.All(m => allowed.Contains(m.Name)));
            Check("R1 implementation adds no mutation surface", typeof(ApplicationDataOrchestrator).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
                .All(m => allowed.Contains(m.Name) || m.Name == "DisposeAsync"));
            var forbidden = new[] { typeof(SystemCore.Core.Cleaner.ICleanerService), typeof(SystemCore.Core.Startup.IStartupMutationService), typeof(IMemoryOptimizationService),
                typeof(SystemCore.Core.Uninstaller.IUninstallExecutor), typeof(SystemCore.Core.Uninstaller.ILeftoverDeletionService) };
            Check("R1 no reachable mutation-service field or constructor dependencies", !HasMutationDependency(typeof(ApplicationDataOrchestrator), forbidden));
            Check("R1 graph detects indirect fields and constructor-only dependencies", HasMutationDependency(typeof(GraphFieldFixture), forbidden) && HasMutationDependency(typeof(GraphConstructorFixture), forbidden));
            Check("R1 graph terminates on safe cycles", !HasMutationDependency(typeof(GraphCycleFixture), forbidden));
            Check("generic disk store is not public", cacheType.IsNestedPrivate && !surface.Any(m => m.IsGenericMethod && m.Name.StartsWith("Cache")));
            var dtoTypes = new[] { typeof(DashboardSummarySnapshot), typeof(SystemInformationSummarySnapshot), typeof(ApplicationsSummarySnapshot), typeof(StartupSummarySnapshot), typeof(CleanerSummarySnapshot) };
            Check("persistent DTOs contain only safe scalars", dtoTypes.All(t => t.IsSealed && t.GetProperties().All(p => p.PropertyType.IsPrimitive || p.PropertyType == typeof(DateTimeOffset))));

            // Actual Core engine, synthetic analysis and owned cache only: no UI, confirmation, files, processes or machine operations.
            // Owner follow-up R1-R1f: explicit memory-optimization confirmation is deferred; this is an observation, not a FIX 7 refusal contract.
            var owner = new FixtureOwner();
            var optimizer = new MemoryOptimizationService(new FixtureAnalysis(), monitor, [owner]);
            var optimization = await optimizer.OptimizeAsync(default);
            Console.WriteLine($"Memory refusal probe: releases={owner.Releases}; result={optimization.Result}; releasedBytes={optimization.KnownReleasedBytes}; confirmation=none; UI=none");
            Console.WriteLine("NOT AVAILABLE in Core-only UnitTests: direct cleaner-clean and uninstall-execution refusal probes (Infrastructure implementations). Verified elsewhere: startup refusal in SystemCore.StartupTests --mutation; leftover deletion rejects non-Safe items at LeftoverDeletionService.cs:68 (source-verified ownership/safety gate, not consent). Direct leftover consent testing remains the R1-R1g follow-up; no project reference/source-link changes authorized.");
        }
        catch (Exception ex) { Check("unexpected fixture failure: " + ex, false); }
        finally { Directory.Delete(root, true); }
        Console.WriteLine($"Orchestration assertions: {passed} passed, {failed} failed, {passed + failed} total");
        return failed == 0;
    }

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static bool HasMutationDependency(Type root, IReadOnlyCollection<Type> forbidden)
    {
        var pending = new Stack<Type>(); var visited = new HashSet<Type>(); pending.Push(root);
        while (pending.TryPop(out var type))
        {
            if (!visited.Add(type)) continue;
            if (forbidden.Any(t => t.IsAssignableFrom(type))) return true;
            if (type.HasElementType) pending.Push(type.GetElementType()!);
            foreach (var argument in type.GetGenericArguments()) pending.Push(argument);
            // Walk declared application dependencies, including container arguments and private/base members.
            // Framework internals and trusted delegate bodies/targets are not an application capability proof.
            if (type.IsGenericParameter || typeof(Delegate).IsAssignableFrom(type) ||
                (type.Assembly != root.Assembly && type.Assembly != typeof(ApplicationDataOrchestrator).Assembly)) continue;
            if (type.BaseType is { } parent) pending.Push(parent);
            const BindingFlags members = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            foreach (var field in type.GetFields(members | BindingFlags.Instance | BindingFlags.Static)) pending.Push(field.FieldType);
            foreach (var parameter in type.GetConstructors(members | BindingFlags.Instance).SelectMany(c => c.GetParameters())) pending.Push(parameter.ParameterType);
        }
        return false;
    }

    private sealed class GraphFieldFixture
    {
        public List<GraphDependencyFixture[]> Dependencies = [];
    }
    private sealed class GraphConstructorFixture
    {
        private GraphConstructorFixture(GraphFieldFixture dependency) { }
    }
    private sealed class GraphDependencyFixture
    {
        public GraphDependencyFixture(IMemoryOptimizationService dependency) { }
    }
    private sealed class GraphCycleFixture
    {
        public GraphCycleFixture? Next = null;
        public List<int[]> Counts = [];
    }
    private sealed class Unserializable { public int Value => throw new InvalidOperationException(); }
    private sealed class FixtureSource : IPerformanceSnapshotSource
    {
        public TaskCompletionSource Sampled { get; } = Signal();
        public bool Detailed { get; private set; }
        public PerformanceSnapshot Sample(bool detailed, CancellationToken token)
        {
            Detailed = detailed; Sampled.TrySetResult();
            return new(DateTimeOffset.UtcNow, new(DateTimeOffset.UtcNow, Reading<double>.Ready(10), Reading<MemoryUsage>.Ready(new(10, 100)), Reading<VolumeUsage>.Missing(), TimeSpan.Zero),
                new(1, 2), new([], true), [], true, null, 4);
        }
        public void Reset() { }
        public void Dispose() { }
    }
    private sealed class FixtureAnalysis : IMemoryAnalysisService
    {
        public MemoryAnalysis Analyze() => new(new(DateTimeOffset.UtcNow, 1000, 10, 900, 1000, 100, 10, 10),
            new(MemoryPressure.High, MemoryAssessmentReason.SustainedPressure, 60), new([], [], false, null));
    }
    private sealed class FixtureOwner : ISystemCoreMemoryCacheOwner
    {
        private readonly Guid version = Guid.NewGuid();
        public int Releases { get; private set; }
        public OwnedMemoryCache Inspect() => new(version, true, true, false, 100);
        public CacheReleaseResult ReleaseIfInactive(Guid expected) { Releases++; return new(CacheReleaseStatus.Released, 100); }
    }
}
