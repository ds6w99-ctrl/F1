using SystemCore.App.Services;
using SystemCore.App.Tools;
using SystemCore.Core.Diagnostics;
using SystemCore.Core.Settings;
using SystemCore.Core.Tools;
using SystemCore.App.Uninstaller;
using SystemCore.App.Startup;
using SystemCore.App.SystemInformation;
using SystemCore.Core.Orchestration;
using SystemCore.Core.Uninstaller;
using SystemCore.Core.Startup;
using SystemCore.Core.SystemInformation;

static class InventoryAutoLoadTests
{
    public static async Task Run(Action<string, bool> check)
    {
        var provider = new Tools();
        await using var orchestrator = InventoryTestOrchestrator.Create();
        using var vm = new ToolsViewModel(new LocalizationService(new Settings()), provider, new Diagnostics(), orchestrator);
        var first = vm.InitializeAsync();
        await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = vm.RefreshAsync();
        check("FIX7 concurrent manual trigger waits for the same inventory", !second.IsCompleted);
        provider.Release.TrySetResult();
        await Task.WhenAll(first, second);
        check("FIX7 concurrent tools triggers invoke provider once", provider.Calls == 1);
        await ExerciseTools(check);
        await ExerciseStartup(check);
        await ExerciseApplications(check);
        await ExerciseInformation(check);
        await CachedDisplay(check);
        await ActivationLifecycle(check);
        await UninstallerCancellationClassification(check);
        await BusyFaultSafety(check);
        var en = new LocalizationService(new Settings());
        var ar = new LocalizationService(new Settings("ar"));
        foreach (var key in new[] { "UnLoadFailed", "StartLoadFailed", "ToolsLoadFailed", "InfoRefreshFailed", "UnLoading", "StartLoading", "ToolsLoading" })
            check("FIX7 bilingual state " + key, en[key] != ar[key] && ar[key].Any(c => c is >= '\u0600' and <= '\u06ff'));
    }
    private static async Task CachedDisplay(Action<string, bool> check)
    {
        await using var apps = InventoryTestOrchestrator.Create();
        apps.CacheApplicationsSummary(new(99, 7, true, DateTimeOffset.UtcNow));
        var ap = new Applications();
        using var avm = new UninstallerViewModel(new(new Settings()), ap, ap, ap, ap, apps, new Diagnostics());
        var a = avm.InitializeAsync(); await ap.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        check("FIX7 applications cached counts display during first read, never actionable", avm.Status.Contains("99") && avm.Inventory.Count == 0 && !avm.CanScanLeftovers);
        ap.Release.TrySetException(new IOException("SECRET cached failure")); await a;
        check("FIX7 applications cached counts survive first read failure", avm.Status.Contains("99") && avm.Status.Contains(avm.Text("UnLoadFailed")));

        await using var startup = InventoryTestOrchestrator.Create();
        startup.CacheStartupSummary(new(77, 60, 17, 0, true, DateTimeOffset.UtcNow));
        var sp = new Startup();
        using var svm = new StartupViewModel(new(new Settings()), sp, sp, startup, new Diagnostics());
        var s = svm.InitializeAsync(); await sp.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        check("FIX7 startup cached counts display during first read, never actionable", svm.StatusText.Contains("77") && !svm.CanEnable && !svm.CanDisable);
        sp.Release.TrySetException(new IOException("SECRET cached failure")); await s;
        check("FIX7 startup cached counts survive first read failure", svm.StatusText.Contains("77") && svm.StatusText.Contains(svm.Text("StartLoadFailed")));

        await using var info = InventoryTestOrchestrator.Create();
        info.CacheSystemInformationSummary(new(96, 1024, 2, DateTimeOffset.UtcNow));
        var ip = new Information(); var dispatcher = new UiDispatcher();
        using var monitor = new DashboardMonitor(new InventoryTestOrchestrator.Monitor(), dispatcher);
        var l = new LocalizationService(new Settings());
        await using var ivm = new SystemInformationViewModel(l, ip, monitor, dispatcher, info, new Diagnostics());
        var i = ivm.InitializeAsync(); await ip.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        check("FIX7 system information cached counts display during first read", ivm.Overview?.Rows.Any(r => r.Value == "96") == true && ivm.IsBusy);
        ip.Release.TrySetException(new IOException("SECRET cached failure")); await i;
        check("FIX7 system information cached counts survive first read failure", ivm.Overview?.Rows.Any(r => r.Value == "96") == true && ivm.LiveValues.Contains(l["InfoRefreshFailed"]));
    }
    private static async Task Exercise<T>(Action<string, bool> check, string name, ApplicationDataOrchestrator orchestrator,
        DataDataset dataset, Probe probe, Func<Task> enter, Func<Task> manual, Func<Task> activate,
        Func<bool> hasData, Func<string> status, string failure, T cached)
    {
        await enter();
        check("FIX7 " + name + " entry loads without a button", probe.Calls == 1 && hasData());
        await enter(); await activate();
        check("FIX7 " + name + " fresh entry/activation suppress producer", probe.Calls == 1);
        await manual();
        check("FIX7 " + name + " manual override bypasses freshness", probe.Calls == 2 && hasData());
        orchestrator.Publish(dataset, cached, TimeSpan.Zero);
        await enter();
        check("FIX7 " + name + " expired entry reloads", probe.Calls == 3 && hasData());
        orchestrator.Publish(dataset, cached, TimeSpan.Zero);
        await activate();
        check("FIX7 " + name + " stale activation reloads", probe.Calls == 4);
        probe.Fail = true;
        await manual();
        check("FIX7 " + name + " failure retains good data", hasData() && status().Contains(failure, StringComparison.Ordinal));
        check("FIX7 " + name + " failure hides exception/path/command", !status().Contains("SECRET", StringComparison.Ordinal));
        probe.Fail = false; probe.Block();
        var first = manual();
        await probe.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = enter(); var third = manual();
        check("FIX7 " + name + " concurrent callers join", !second.IsCompleted && !third.IsCompleted);
        probe.Release.TrySetResult();
        await Task.WhenAll(first, second, third);
        check("FIX7 " + name + " single-flight provider", probe.Calls == 6);
    }
    private static async Task ExerciseTools(Action<string, bool> check)
    {
        await using var o = InventoryTestOrchestrator.Create(); var p = new Tools(); p.Release.TrySetResult();
        using var vm = new ToolsViewModel(new(new Settings()), p, new Diagnostics(), o);
        await Exercise(check, "Tools", o, DataDataset.Tools, p, vm.InitializeAsync, vm.RefreshAsync, vm.RefreshIfStaleAsync,
            () => vm.Items.Count == 1, () => vm.Status, vm.Text("ToolsLoadFailed"), (IReadOnlyList<WindowsToolItem>)p.Value);
        await using var warm = InventoryTestOrchestrator.Create(); var provider = new Tools(); provider.Release.TrySetResult();
        using var warmed = new ToolsViewModel(new(new Settings()), provider, new Diagnostics(), warm);
        await warm.StartAsync(); await warmed.InitializeAsync();
        check("FIX7 Tools startup and entry share load", provider.Calls == 1 && warmed.Items.Count == 1);
    }
    private static async Task ExerciseStartup(Action<string, bool> check)
    {
        await using var o = InventoryTestOrchestrator.Create(); var p = new Startup(); p.Release.TrySetResult();
        using var vm = new StartupViewModel(new(new Settings()), p, p, o, new Diagnostics());
        await Exercise(check, "Startup", o, DataDataset.Startup, p, vm.InitializeAsync, vm.RefreshAsync, vm.RefreshIfStaleAsync,
            () => vm.Rows.Count == 1, () => vm.StatusText, vm.Text("StartLoadFailed"), p.Value);
        var before = p.Calls; vm.SelectedId = p.Item.Id;
        await vm.ChangeAsync(StartupAction.Disable, p.Item.Id);
        check("FIX7 startup mutation forces fresh inventory", p.Mutations == 1 && p.Calls == before + 1 && vm.Rows.Single().Status == StartupStatus.DisabledBySystemCore);
        p.Fail = true; vm.SelectedId = p.Item.Id;
        await vm.ChangeAsync(StartupAction.Enable, p.Item.Id);
        check("FIX7 post-mutation read failure retains rows and reports refresh failure", vm.Rows.Count == 1 && vm.StatusText == vm.Text("StartLoadFailed"));
        vm.SelectedId = p.Item.Id;
        check("FIX7 retained startup display cannot authorize another mutation", !vm.CanDisable && !vm.CanEnable);
        p.Fail = false;
        await vm.RefreshAsync(); before = p.Calls; vm.SelectedId = p.Item.Id; p.CancelMutation = true;
        await vm.ChangeAsync(StartupAction.Enable, p.Item.Id); await vm.InitializeAsync();
        check("FIX7 cancelled startup operation cannot reuse a consumed cached session", p.Calls == before + 1);
        await using var warm = InventoryTestOrchestrator.Create(); var provider = new Startup(); provider.Release.TrySetResult();
        using var warmed = new StartupViewModel(new(new Settings()), provider, provider, warm, new Diagnostics());
        await warm.StartAsync(); await warmed.InitializeAsync();
        check("FIX7 Startup startup and entry share load", provider.Calls == 1 && warmed.Rows.Count == 1 && provider.Mutations == 0);
    }
    private static async Task ExerciseApplications(Action<string, bool> check)
    {
        await using var o = InventoryTestOrchestrator.Create(); var p = new Applications(); p.Release.TrySetResult();
        using var vm = new UninstallerViewModel(new(new Settings()), p, p, p, p, o, new Diagnostics());
        await Exercise(check, "Applications", o, DataDataset.InstalledApplications, p, vm.InitializeAsync, vm.RefreshAsync, vm.RefreshIfStaleAsync,
            () => vm.Rows.Count == 1, () => vm.Status, vm.Text("UnLoadFailed"), p.Value);
        p.Outcome = UninstallState.Cancelled; vm.SelectedId = p.Item.Id;
        await vm.UninstallConfirmedAsync(p.Item.Id); var before = p.Calls;
        await vm.InitializeAsync();
        check("FIX7 cancelled uninstall cannot reuse a consumed cached session", p.Calls == before + 1 && vm.Inventory.Count == 1);
        p.Outcome = UninstallState.Removed; before = p.Calls; vm.SelectedId = p.Item.Id;
        await vm.UninstallConfirmedAsync(p.Item.Id);
        check("FIX7 verified uninstall forces inventory and retains automatic scan", p.Calls == before + 1 && p.Scans == 1 && vm.ShowLeftovers && vm.Inventory.Count == 0);
        var summary = await o.GetAsync<ApplicationsSummarySnapshot>(DataDataset.Games, LoadTier.Background, UninstallerViewModel.GamesFreshness,
            _ => throw new InvalidOperationException("Summary should be fresh"), default);
        check("FIX7 uninstall refreshes games summary too", summary.Applications == 0 && summary.Games == 0);
        vm.SelectLeftover(p.Leftover.Id, true); before = p.Calls;
        await vm.DeleteLeftoversConfirmedAsync([p.Leftover.Id]);
        check("FIX7 confirmed leftover deletion forces refresh", p.Deletions == 1 && p.Calls == before + 1 && vm.ShowLeftovers);
        await using var warm = InventoryTestOrchestrator.Create(); var provider = new Applications(); provider.Release.TrySetResult();
        using var warmed = new UninstallerViewModel(new(new Settings()), provider, provider, provider, provider, warm, new Diagnostics());
        await warm.StartAsync(); await warmed.InitializeAsync();
        check("FIX7 apps/games warmup and entry enumerate once without actions", provider.Calls == 1 && warmed.Rows.Count == 1 && provider.Scans == 0 && provider.Deletions == 0);
        await using var failed = InventoryTestOrchestrator.Create(); var game = new Applications(); game.Release.TrySetResult();
        game.Item = game.Item with { ItemKind = ItemKind.Game };
        using var lastGood = new UninstallerViewModel(new(new Settings()), game, game, game, game, failed, new Diagnostics());
        await lastGood.InitializeAsync(); game.Fail = true; lastGood.SelectedId = game.Item.Id;
        await lastGood.UninstallConfirmedAsync(game.Item.Id);
        check("FIX7 native-game post-removal read failure retains last display", lastGood.Rows.Count == 1 && lastGood.InventoryLoadFailed && !lastGood.ShowLeftovers);
        lastGood.SelectedId = game.Item.Id;
        check("FIX7 retained applications display cannot authorize another uninstall", lastGood.Selected is null);
    }
    private static async Task ExerciseInformation(Action<string, bool> check)
    {
        await using var o = InventoryTestOrchestrator.Create(); var p = new Information(); p.Release.TrySetResult();
        var l = new LocalizationService(new Settings()); var dispatcher = new UiDispatcher();
        using var monitor = new DashboardMonitor(new InventoryTestOrchestrator.Monitor(), dispatcher);
        await using var vm = new SystemInformationViewModel(l, p, monitor, dispatcher, o, new Diagnostics());
        await Exercise(check, "SystemInformation", o, DataDataset.SystemInformation, p, vm.InitializeAsync,
            () => vm.RefreshCommand.ExecuteAsync(null), vm.RefreshIfStaleAsync,
            () => vm.Overview?.Rows.Any(r => r.Value == "Fixture Device") == true, () => vm.LiveValues, l["InfoRefreshFailed"], p.Value);
        await using var warm = InventoryTestOrchestrator.Create(); var provider = new Information(); provider.Release.TrySetResult();
        await using var warmed = new SystemInformationViewModel(l, provider, monitor, dispatcher, warm, new Diagnostics());
        await warm.StartAsync(); warmed.SetActive(true); await warmed.InitializeAsync();
        check("FIX7 SystemInformation existing activation shares warmup", provider.Calls == 1 && warmed.Groups.Count > 0);
    }
    // D1/D2/D3: the activation path must not start duplicate work, must not start work after
    // shutdown or disposal, and must classify cancellation and disposal differently from a
    // genuine provider failure.
    private static async Task ActivationLifecycle(Action<string, bool> check)
    {
        var always = new DatasetRefreshRequest(DataDataset.Tools, TimeSpan.Zero, _ => Task.CompletedTask);

        // 1. Activate during shutdown.
        await using (var o = InventoryTestOrchestrator.Create())
        {
            var p = new Tools(); p.Release.TrySetResult();
            using var vm = new ToolsViewModel(new(new Settings()), p, new Diagnostics(), o);
            await vm.InitializeAsync();
            await o.DisposeAsync();
            var started = await o.RefreshStaleAsync([always with { Refresh = _ => vm.RefreshIfStaleAsync() }]);
            check("FIX7 activation after shutdown starts no refresh", started == 0 && p.Calls == 1);
        }

        // 2. Activate after lifetime cancellation.
        await using (var o = InventoryTestOrchestrator.Create())
        {
            using var cts = new CancellationTokenSource(); cts.Cancel();
            var p = new Tools(); p.Release.TrySetResult();
            var started = await o.RefreshStaleAsync([always with { Refresh = _ => { p.Calls++; return Task.CompletedTask; } }], cts.Token);
            check("FIX7 activation after lifetime cancellation starts no refresh", started == 0 && p.Calls == 0);
        }

        // 3. View model disposed before activation. The lifetime cancel alone already stops the
        // provider call, so asserting Calls == 0 would not isolate the disposed guard. What the
        // guard uniquely prevents is any state mutation on a dead view model, so that is asserted.
        await using (var o = InventoryTestOrchestrator.Create())
        {
            var p = new Tools(); p.Release.TrySetResult();
            var vm = new ToolsViewModel(new(new Settings()), p, new Diagnostics(), o);
            var notifications = 0;
            vm.PropertyChanged += (_, _) => Interlocked.Increment(ref notifications);
            vm.Dispose();
            await o.RefreshStaleAsync([always with { Refresh = _ => vm.RefreshIfStaleAsync() }]);
            await Task.Delay(50);
            check("FIX7 disposed view model starts no provider work and mutates no state",
                p.Calls == 0 && notifications == 0);
        }

        // 4. Twenty-four minimize/restore cycles must not repeat the work.
        await using (var o = InventoryTestOrchestrator.Create())
        {
            var p = new Tools(); p.Release.TrySetResult();
            using var vm = new ToolsViewModel(new(new Settings()), p, new Diagnostics(), o);
            await vm.InitializeAsync();
            var passes = 0;
            for (var cycle = 0; cycle < 24; cycle++)
                passes += await o.RefreshStaleAsync([always with { StaleAfter = ToolsViewModel.ToolsFreshness, Refresh = _ => vm.RefreshIfStaleAsync() }]);
            check("FIX7 24 restore cycles start no duplicate refresh", passes == 0 && p.Calls == 1);
        }

        // 5. Stale data triggers exactly one refresh.
        await using (var o = InventoryTestOrchestrator.Create())
        {
            var p = new Tools();
            using var vm = new ToolsViewModel(new(new Settings()), p, new Diagnostics(), o);
            var request = always with { Refresh = _ => vm.RefreshIfStaleAsync() };
            var first = o.RefreshStaleAsync([request]);
            await p.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var second = await o.RefreshStaleAsync([request]);
            p.Release.TrySetResult();
            var count = await first;
            check("FIX7 stale data triggers exactly one refresh", count == 1 && second == 0 && p.Calls == 1);
        }

        // 6. Fresh data does not refresh.
        await using (var o = InventoryTestOrchestrator.Create())
        {
            var p = new Tools(); p.Release.TrySetResult();
            using var vm = new ToolsViewModel(new(new Settings()), p, new Diagnostics(), o);
            await vm.InitializeAsync();
            var started = await o.RefreshStaleAsync([always with { StaleAfter = TimeSpan.FromMinutes(30), Refresh = _ => vm.RefreshIfStaleAsync() }]);
            check("FIX7 fresh data does not refresh on activation", started == 0 && p.Calls == 1);
        }

        // 7. Duplicate activation does not start duplicate providers. All four callers must be
        // awaiting the same single load, not merely avoid a second provider call by accident.
        await using (var o = InventoryTestOrchestrator.Create())
        {
            var p = new Tools();
            using var vm = new ToolsViewModel(new(new Settings()), p, new Diagnostics(), o);
            var calls = new[] { vm.RefreshIfStaleAsync(), vm.RefreshIfStaleAsync(), vm.RefreshIfStaleAsync(), vm.RefreshIfStaleAsync() };
            await p.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            check("FIX7 duplicate activation joins one in-flight load",
                p.Calls == 1 && calls.All(c => !c.IsCompleted));
            p.Release.TrySetResult();
            await Task.WhenAll(calls);
            check("FIX7 duplicate activation starts one provider read", p.Calls == 1);
        }

        // 8. Expected cancellation is not an error and does not strand the dataset.
        await using (var o = InventoryTestOrchestrator.Create())
        {
            var calls = 0; var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var cancelled = always with { Refresh = async _ => { Interlocked.Increment(ref calls); entered.TrySetResult(); await Task.Yield(); throw new OperationCanceledException(); } };
            await o.RefreshStaleAsync([cancelled]);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var blocked = await o.RefreshStaleAsync([cancelled]);
            check("FIX7 expected cancellation does not start a second run while in flight", blocked == 0 && calls == 1);
            // A cancelled action must release the dataset so a later activation can run again.
            // Admission and execution are separate facts, so both are observed.
            var ranAgain = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var second = always with { Refresh = async _ => { Interlocked.Increment(ref calls); ranAgain.TrySetResult(); await Task.Yield(); throw new OperationCanceledException(); } };
            var admitted = 0;
            for (var spin = 0; spin < 100 && admitted == 0; spin++) { admitted = await o.RefreshStaleAsync([second]); if (admitted == 0) await Task.Delay(20); }
            await ranAgain.Task.WaitAsync(TimeSpan.FromSeconds(5));
            check("FIX7 expected cancellation is not an error and releases the dataset", admitted == 1 && calls == 2);
        }

        // 8b. A genuine user cancel comes from the UI cancel button, which cancels the active
        // operation and not the lifetime. It must read as cancellation, not as a failed load.
        await using (var o = InventoryTestOrchestrator.Create())
        {
            var p = new Startup();
            var log = new Diagnostics();
            using var vm = new StartupViewModel(new(new Settings()), p, p, o, log);
            var load = vm.InitializeAsync();
            await p.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            vm.Cancel();
            p.Release.TrySetResult();
            await load;
            check("FIX7 a genuine user cancel is cancellation, not a provider failure",
                vm.StatusText == vm.Text("StartCancelled") && log.ProviderFailures == 0);
        }
        await using (var o = InventoryTestOrchestrator.Create())
        {
            var p = new Applications();
            var log = new Diagnostics();
            using var vm = new UninstallerViewModel(new(new Settings()), p, p, p, p, o, log);
            var load = vm.InitializeAsync();
            await p.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            vm.CancelOperation();
            p.Release.TrySetResult();
            await load;
            check("FIX7 a genuine uninstall cancel is cancellation, not a provider failure",
                !vm.InventoryLoadFailed && log.ProviderFailures == 0);
        }
        // 8c. A dataset queued behind a still-running one must not start after the caller's
        // token is cancelled. Admission and execution are separated in time, so the token is
        // re-checked per item; without that, a queued dataset starts work after shutdown began.
        await using (var o = InventoryTestOrchestrator.Create())
        {
            var first = 0; var second = 0;
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var a = always with { Dataset = DataDataset.Tools, Refresh = async _ => { Interlocked.Increment(ref first); entered.TrySetResult(); await gate.Task; } };
            var b = always with { Dataset = DataDataset.Startup, Refresh = _ => { Interlocked.Increment(ref second); return Task.CompletedTask; } };
            using var cts = new CancellationTokenSource();
            var admitted = await o.RefreshStaleAsync([a, b], cts.Token);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            cts.Cancel();
            gate.SetResult();
            // Wait until the cancelled pass has drained, proven by Tools becoming admissible
            // again, so a second that has not started is a decision rather than a timing guess.
            var drained = false;
            for (var spin = 0; spin < 200 && !drained; spin++)
            {
                drained = await o.RefreshStaleAsync([always with { Dataset = DataDataset.Tools, Refresh = _ => Task.CompletedTask }]) == 1;
                if (!drained) await Task.Delay(20);
            }
            check("FIX7 a dataset queued behind a cancelled refresh is never started", admitted == 2 && drained && first == 1 && second == 0);
        }

        // 8d. A caller-supplied delegate that fails for its own reason is a real defect. The
        // orchestration layer must record it rather than absorb it into a discarded task.
        await using (var log = new Diagnostics())
        {
            await using var o = InventoryTestOrchestrator.Create(log);
            var broken = always with { Refresh = _ => throw new InvalidOperationException("fixture") };
            var admitted = await o.RefreshStaleAsync([broken]);
            var reported = true;
            // Reported as a failed check rather than a harness crash, so removing the
            // production record surfaces as FAIL instead of an unhandled timeout.
            try { await log.FirstProviderFailure.WaitAsync(TimeSpan.FromSeconds(30)); }
            catch (TimeoutException) { reported = false; }
            check("FIX7 a failing activation refresh is recorded, not absorbed", admitted == 1 && reported && log.ProviderFailures > 0);
        }

        // 8e. A finishing refresh must never clear a newer call's in-flight mark. Without
        // ticket matching, the first call's cleanup wipes the second call's mark while it is
        // still running, and a third activation can then start a concurrent duplicate.
        await using (var o = InventoryTestOrchestrator.Create())
        {
            var aRuns = 0; var bRuns = 0;
            var aEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var bEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var aGate1 = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var aGate2 = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var bGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var secondEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var firstA = always with { Dataset = DataDataset.Tools, Refresh = async _ => { Interlocked.Increment(ref aRuns); aEntered.TrySetResult(); await aGate1.Task; } };
            var firstB = always with { Dataset = DataDataset.Startup, Refresh = async _ => { Interlocked.Increment(ref bRuns); bEntered.TrySetResult(); await bGate.Task; } };
            var secondA = always with { Dataset = DataDataset.Tools, Refresh = async _ => { Interlocked.Increment(ref aRuns); secondEntered.TrySetResult(); await aGate2.Task; } };
            _ = o.RefreshStaleAsync([firstA, firstB]);
            // Items run sequentially, so A must be released before B can be reached.
            await aEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            aGate1.SetResult();
            await bEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            // A's mark is released once A completes, so a second activation can legitimately
            // take it over while the first pass is still running. Signalled by the action
            // itself rather than a fixed budget, because the hand-off needs the first pass's
            // thread to resume, and that latency is not bounded on a loaded machine.
            var tookOver = false;
            for (var spin = 0; spin < 600 && !tookOver; spin++)
            {
                await o.RefreshStaleAsync([secondA]);
                tookOver = secondEntered.Task.IsCompleted;
                if (!tookOver) await Task.Delay(25);
            }
            // Let the first call drain. Its cleanup must not clear the second call's mark.
            bGate.SetResult();
            // Prove the first pass has fully drained by waiting until its Startup mark becomes
            // available again, instead of guessing with a fixed delay. At that instant the
            // Tools mark must still belong to the second call, so a third pass is refused.
            var drained = false;
            for (var spin = 0; spin < 200 && !drained; spin++)
            {
                drained = await o.RefreshStaleAsync([always with { Dataset = DataDataset.Startup, Refresh = _ => Task.CompletedTask }]) == 1;
                if (!drained) await Task.Delay(20);
            }
            var third = await o.RefreshStaleAsync([always with { Dataset = DataDataset.Tools, Refresh = _ => { Interlocked.Increment(ref aRuns); return Task.CompletedTask; } }]);
            check("FIX7 a finishing refresh never clears a newer call's in-flight mark",
                drained && bRuns == 1 && aRuns == 2 && third == 0);
            aGate2.SetResult();
        }

        // 8f. The requirement treats a genuinely disposed service as an expected disposal.
        // Both remaining pages must honour that, or a teardown order inside the DI container
        // would report a provider failure on every shutdown.
        await using (var o = InventoryTestOrchestrator.Create())
        {
            var p = new Applications(); p.Release.TrySetResult();
            var log = new Diagnostics();
            using var vm = new UninstallerViewModel(new(new Settings()), p, p, p, p, o, log);
            await o.DisposeAsync();
            await vm.RefreshAsync();
            check("FIX7 a disposed service is not a provider failure on the applications page",
                !vm.InventoryLoadFailed && log.ProviderFailures == 0);
        }
        await using (var o = InventoryTestOrchestrator.Create())
        {
            var p = new Information(); p.Release.TrySetResult();
            var l = new LocalizationService(new Settings()); var dispatcher = new UiDispatcher();
            using var monitor = new DashboardMonitor(new InventoryTestOrchestrator.Monitor(), dispatcher);
            var log = new Diagnostics();
            await using var vm = new SystemInformationViewModel(l, p, monitor, dispatcher, o, log);
            await o.DisposeAsync();
            await vm.RefreshCommand.ExecuteAsync(null);
            check("FIX7 a disposed service is not a provider failure on the information page",
                !vm.LiveValues.Contains(l["InfoRefreshFailed"], StringComparison.Ordinal) && log.ProviderFailures == 0);
        }

        // 8g. The orchestration layer's own filters must be narrow in both directions.
        // 8g-1: a cancellation nobody asked for is a real defect, not an expected teardown.
        await using (var log = new Diagnostics())
        {
            await using var o = InventoryTestOrchestrator.Create(log);
            var spurious = always with { Refresh = _ => throw new OperationCanceledException("nobody asked") };
            var admitted = await o.RefreshStaleAsync([spurious]);
            var reported = true;
            // Generous bound: the record arrives on a thread-pool thread, and this suite parks
            // many refreshes deliberately, so scheduling latency on a loaded machine is unbounded.
            // The wait still ends the instant the record lands, so a healthy run costs nothing.
            try { await log.FirstProviderFailure.WaitAsync(TimeSpan.FromSeconds(30)); }
            catch (TimeoutException) { reported = false; }
            check("FIX7 a cancellation nobody requested is recorded, not treated as teardown",
                admitted == 1 && reported && log.ProviderFailures == 1);
        }

        // 8g-2: a cancellation the caller did ask for is teardown, not a defect. The delegate
        // cancels the caller's token and throws in the same breath, so the filter is reached with
        // that token definitely cancelled. The wait is anchored on the delegate actually running
        // rather than on a sleep, so "nothing was reported" is an observation, not a guess.
        await using (var log = new Diagnostics())
        {
            await using var o = InventoryTestOrchestrator.Create(log);
            using var cts = new CancellationTokenSource();
            var asked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var request = always with
            {
                Dataset = DataDataset.Tools,
                Refresh = _ => { cts.Cancel(); asked.TrySetResult(); throw new OperationCanceledException("caller went away"); }
            };
            await o.RefreshStaleAsync([request], cts.Token);
            await asked.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await Task.Delay(500);
            check("FIX7 a cancellation the caller asked for is not reported as a failure",
                !o.IsShutdown && log.ProviderFailures == 0);
        }

        // 8g-3: a disposal of something that was never disposed is an unexpected failure and
        // must be recorded. This is the other direction of the ObjectDisposedException filter:
        // without it the filter would be too permissive and hide genuine defects.
        await using (var log = new Diagnostics())
        {
            await using var o = InventoryTestOrchestrator.Create(log);
            var premature = always with { Refresh = _ => throw new ObjectDisposedException("nothing was disposed") };
            var admitted = await o.RefreshStaleAsync([premature]);
            var reported = true;
            try { await log.FirstProviderFailure.WaitAsync(TimeSpan.FromSeconds(30)); }
            catch (TimeoutException) { reported = false; }
            check("FIX7 a disposal of something that is not disposed is recorded as unexpected",
                admitted == 1 && reported && log.ProviderFailures == 1);
        }

        // 8h. A refresh that reaches the disposal window is teardown, not a defect. This is the
        // one scenario the ObjectDisposedException filter exists for: the orchestrator has set its
        // disposed flag, and a provider still in flight reads the cache and is refused. Without
        // the filter, every shutdown that races an in-flight refresh would report a provider
        // failure. The delegate calls the real read, so the disposal it hits is genuine.
        await using (var log = new Diagnostics())
        {
            await using var o = InventoryTestOrchestrator.Create(log);
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var teardown = always with
            {
                Dataset = DataDataset.Tools,
                Refresh = async _ =>
                {
                    entered.TrySetResult();
                    try { await gate.Task; await o.GetAsync(DataDataset.Tools, LoadTier.Cached, TimeSpan.Zero, _ => Task.FromResult(0), default); }
                    finally { done.TrySetResult(); }
                }
            };
            await o.RefreshStaleAsync([teardown]);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            // Started but not awaited, so the delegate can reach its read while the orchestrator
            // already reports itself disposed. Disposal is idempotent.
            var disposal = o.DisposeAsync();
            gate.SetResult();
            await done.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await disposal;
            await Task.Delay(300);
            check("FIX7 a refresh that reaches the disposal window is teardown, not a defect",
                o.IsShutdown && log.ProviderFailures == 0);
        }

        // 9. A disposal failure is expected only when the service really is disposed. The
        // requirement lists "shutdown began" and "the service is genuinely disposed" as
        // expected; anything else must be surfaced and recorded, never absorbed.
        await using (var o = InventoryTestOrchestrator.Create())
        {
            var p = new Tools(); p.Release.TrySetResult();
            var log = new Diagnostics();
            using var vm = new ToolsViewModel(new(new Settings()), p, log, o);
            await o.DisposeAsync();
            await vm.RefreshIfStaleAsync();
            check("FIX7 disposal of a genuinely disposed service is not a provider failure",
                vm.Status != vm.Text("ToolsLoadFailed") && log.ProviderFailures == 0);
        }
        await using (var o = InventoryTestOrchestrator.Create())
        {
            var p = new Tools(); p.Release.TrySetResult(); p.Fault = new ObjectDisposedException("fixture");
            var log = new Diagnostics();
            using var vm = new ToolsViewModel(new(new Settings()), p, log, o);
            await vm.RefreshIfStaleAsync();
            check("FIX7 unexpected ObjectDisposedException is surfaced and recorded",
                vm.Status == vm.Text("ToolsLoadFailed") && log.ProviderFailures > 0);
        }
    }

    // The uninstaller's own exception classification. Every site here is a user-visible mutation,
    // so the difference between "you cancelled" and "something broke" is the difference between an
    // honest status and a lie the user acts on. Each case below is one classification decision.
    private static async Task UninstallerCancellationClassification(Action<string, bool> check)
    {
        // 8i-1. A genuine user cancel: the token the operation was actually given is cancelled, and
        // the executor reports it by throwing. This is the only shape that may read as Cancelled.
        await using (var o = InventoryTestOrchestrator.Create())
        {
            var p = new Applications();
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            p.Uninstall = async _ => { entered.TrySetResult(); await gate.Task; throw new OperationCanceledException("user dismissed UAC"); };
            var log = new Diagnostics();
            using var vm = await LoadedUninstaller(p, o, log);
            var run = vm.UninstallConfirmedAsync(p.Item.Id);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            // The cancel button, not Dispose: the lifetime must stay live for this to be a user cancel.
            vm.CancelOperation();
            gate.SetResult();
            await run.WaitAsync(TimeSpan.FromSeconds(10));
            check("FIX7 a cancelled uninstall is reported as cancelled, not as a failure",
                vm.Status == vm.Text("UnStateCancelled") && log.ProviderFailures == 0);
        }

        // 8i-2. The same exception type with no cancelled token. Nothing the user did, and nothing
        // shut down, so reporting it as a cancel is the exact failure mode under test: the status
        // told the user they had cancelled work that actually failed.
        await using (var o = InventoryTestOrchestrator.Create())
        {
            var p = new Applications();
            p.Uninstall = _ => throw new OperationCanceledException("nobody asked for this");
            var log = new Diagnostics();
            using var vm = await LoadedUninstaller(p, o, log);
            await vm.UninstallConfirmedAsync(p.Item.Id).WaitAsync(TimeSpan.FromSeconds(10));
            check("FIX7 an OperationCanceledException with no cancelled token is a failure, not a cancel",
                vm.Status == vm.Text("UnStateFailed") && log.ProviderFailures == 1);
        }

        // 8i-3. A disposal met once teardown has genuinely begun is expected. Disposal happens
        // before the window closes, so the view model is still live here and only the orchestrator
        // reports itself gone. Nothing may be recorded, and nothing may escape the handler.
        await using (var o = InventoryTestOrchestrator.Create())
        {
            var p = new Applications();
            p.Uninstall = _ => throw new ObjectDisposedException("orchestrator");
            var log = new Diagnostics();
            using var vm = await LoadedUninstaller(p, o, log);
            await o.DisposeAsync();
            await vm.UninstallConfirmedAsync(p.Item.Id).WaitAsync(TimeSpan.FromSeconds(10));
            check("FIX7 a disposal during shutdown is expected, not recorded as a failure",
                o.IsShutdown && vm.Status == vm.Text("UnStateCancelled") && log.ProviderFailures == 0);
        }

        // 8i-4. A fault that is not a known provider failure is a real defect. It must be recorded
        // and stay Failed rather than being absorbed into a status nobody investigates.
        await using (var o = InventoryTestOrchestrator.Create())
        {
            var p = new Applications();
            p.Uninstall = _ => throw new InvalidCastException("fixture defect");
            var log = new Diagnostics();
            using var vm = await LoadedUninstaller(p, o, log);
            await vm.UninstallConfirmedAsync(p.Item.Id).WaitAsync(TimeSpan.FromSeconds(10));
            check("FIX7 an unexpected uninstall failure is recorded and stays failed",
                vm.Status == vm.Text("UnStateFailed") && log.ProviderFailures == 1);
        }

        // 8i-5. A native uninstaller failure is an expected outcome of the executor, reported as
        // Failed and not recorded. It is deliberately distinguishable from 8i-4: this one is the
        // documented failure mode of the operation, that one is a defect in the plumbing.
        await using (var o = InventoryTestOrchestrator.Create())
        {
            var p = new Applications();
            p.Uninstall = _ => throw new System.ComponentModel.Win32Exception(5);
            var log = new Diagnostics();
            using var vm = await LoadedUninstaller(p, o, log);
            await vm.UninstallConfirmedAsync(p.Item.Id).WaitAsync(TimeSpan.FromSeconds(10));
            check("FIX7 a native uninstaller failure is failed without being recorded as a defect",
                vm.Status == vm.Text("UnStateFailed") && log.ProviderFailures == 0);
        }

        // 8i-6. The UAC cancel the user actually performs: the executor completes normally and
        // reports Cancelled as its result. No exception is involved, and crucially no receipt
        // exists, so the leftover scan must not run and nothing may be presented as removable.
        await using (var o = InventoryTestOrchestrator.Create())
        {
            var p = new Applications();
            p.Uninstall = _ => Task.FromResult(new UninstallOutcome(UninstallState.Cancelled, p.Item.Id, null, "uac dismissed", null));
            var log = new Diagnostics();
            using var vm = await LoadedUninstaller(p, o, log);
            await vm.UninstallConfirmedAsync(p.Item.Id).WaitAsync(TimeSpan.FromSeconds(10));
            check("FIX7 a UAC cancel is cancelled, scans nothing and records nothing",
                vm.Status == vm.Text("UnStateCancelled") && p.Scans == 0 && !vm.CanScanLeftovers && log.ProviderFailures == 0);
        }

        // 8i-7. The scan that follows a successful removal is optional and read-only. A fault with
        // no cancelled token used to land in a bare catch that reported the application as Removed.
        // Claiming a verified removal that never happened is the worst lie this view model can tell,
        // so it is asserted directly.
        await using (var o = InventoryTestOrchestrator.Create())
        {
            var p = new Applications();
            p.Uninstall = _ => Task.FromResult(new UninstallOutcome(UninstallState.Removed, p.Item.Id, 0, null,
                new(Guid.NewGuid(), p.Item.Id, p.Item.DisplayName, DateTimeOffset.UtcNow)));
            p.Scan = _ => throw new OperationCanceledException("nobody asked for this");
            var log = new Diagnostics();
            using var vm = await LoadedUninstaller(p, o, log);
            await vm.UninstallConfirmedAsync(p.Item.Id).WaitAsync(TimeSpan.FromSeconds(10));
            check("FIX7 a faulty leftover scan is recorded and never claims the application was removed",
                vm.Status == vm.Text("UnStateFailed") && log.ProviderFailures == 1);
        }

        // A genuine cancel of the same scan keeps the pre-existing meaning: the removal itself was
        // verified, and only the optional cleanup review is absent.
        await using (var o = InventoryTestOrchestrator.Create())
        {
            var p = new Applications();
            p.Uninstall = _ => Task.FromResult(new UninstallOutcome(UninstallState.Removed, p.Item.Id, 0, null,
                new(Guid.NewGuid(), p.Item.Id, p.Item.DisplayName, DateTimeOffset.UtcNow)));
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            p.Scan = async token => { entered.TrySetResult(); await gate.Task; throw new OperationCanceledException(token); };
            var log = new Diagnostics();
            using var vm = await LoadedUninstaller(p, o, log);
            var run = vm.UninstallConfirmedAsync(p.Item.Id);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            vm.CancelOperation();
            gate.SetResult();
            await run.WaitAsync(TimeSpan.FromSeconds(10));
            check("FIX7 a cancelled leftover scan keeps the verified removal and records nothing",
                vm.Status == vm.Text("UnStateRemoved") && log.ProviderFailures == 0);
        }

        // 8i-8. Shutdown alone is not evidence of cancellation. An OperationCanceledException
        // with no cancelled token that arrives while the orchestrator is shutting down is a
        // provider fault, not a user cancel. The dispose happens inside the delegate so the
        // pre-flight Publish has already succeeded and the injected fault is actually reached.
        await using (var o = InventoryTestOrchestrator.Create())
        {
            var p = new Applications();
            p.Uninstall = async _ => { await o.DisposeAsync(); throw new OperationCanceledException("shutdown raced, nobody cancelled"); };
            var log = new Diagnostics();
            using var vm = await LoadedUninstaller(p, o, log);
            await vm.UninstallConfirmedAsync(p.Item.Id).WaitAsync(TimeSpan.FromSeconds(10));
            check("FIX7 an uncancelled uninstall cancel during shutdown is a failure, not a cancel",
                o.IsShutdown && vm.Status == vm.Text("UnStateFailed") && log.ProviderFailures == 1);
        }

        // 8i-9. Same classification on the deletion path. The setup uninstall uses a verified
        // removal so the leftover session exists; the fault is injected only on deletion.
        await using (var o = InventoryTestOrchestrator.Create())
        {
            var p = new Applications();
            p.Uninstall = _ => Task.FromResult(new UninstallOutcome(UninstallState.Removed, p.Item.Id, 0, null,
                new(Guid.NewGuid(), p.Item.Id, p.Item.DisplayName, DateTimeOffset.UtcNow)));
            var log = new Diagnostics();
            using var vm = await LoadedUninstaller(p, o, log);
            await vm.UninstallConfirmedAsync(p.Item.Id).WaitAsync(TimeSpan.FromSeconds(10));
            vm.SelectLeftover(p.Leftover.Id, true);
            await o.DisposeAsync();
            p.Delete = _ => throw new OperationCanceledException("shutdown raced, nobody cancelled");
            await vm.DeleteLeftoversConfirmedAsync([p.Leftover.Id]).WaitAsync(TimeSpan.FromSeconds(10));
            check("FIX7 an uncancelled deletion cancel during shutdown is a failure, not a cancel",
                o.IsShutdown && vm.Status == vm.Text("UnStateFailed") && log.ProviderFailures == 1);
        }

        // 8i-10. ObjectDisposedException is expected only during teardown. Outside shutdown it
        // is an unexpected provider fault: Failed and recorded. It previously fell into the
        // known InvalidOperationException catch (ObjectDisposedException derives from it) and
        // was swallowed without a record.
        await using (var o = InventoryTestOrchestrator.Create())
        {
            var p = new Applications();
            p.Uninstall = _ => throw new ObjectDisposedException("nothing was disposed");
            var log = new Diagnostics();
            using var vm = await LoadedUninstaller(p, o, log);
            await vm.UninstallConfirmedAsync(p.Item.Id).WaitAsync(TimeSpan.FromSeconds(10));
            check("FIX7 an unexpected uninstall disposal outside shutdown is recorded and stays failed",
                !o.IsShutdown && vm.Status == vm.Text("UnStateFailed") && log.ProviderFailures == 1);
        }

        // 8i-11. Same unexpected-disposal classification on the deletion path.
        await using (var o = InventoryTestOrchestrator.Create())
        {
            var p = new Applications();
            p.Uninstall = _ => Task.FromResult(new UninstallOutcome(UninstallState.Removed, p.Item.Id, 0, null,
                new(Guid.NewGuid(), p.Item.Id, p.Item.DisplayName, DateTimeOffset.UtcNow)));
            var log = new Diagnostics();
            using var vm = await LoadedUninstaller(p, o, log);
            await vm.UninstallConfirmedAsync(p.Item.Id).WaitAsync(TimeSpan.FromSeconds(10));
            vm.SelectLeftover(p.Leftover.Id, true);
            p.Delete = _ => throw new ObjectDisposedException("nothing was disposed");
            await vm.DeleteLeftoversConfirmedAsync([p.Leftover.Id]).WaitAsync(TimeSpan.FromSeconds(10));
            check("FIX7 an unexpected deletion disposal outside shutdown is recorded and stays failed",
                !o.IsShutdown && vm.Status == vm.Text("UnStateFailed") && log.ProviderFailures == 1);
        }
    }

    // D-a: a PropertyChanged subscriber that faults while Busy is being raised must not wedge
    // the view model. EndOperation is the only writer of Busy = false, and StartLoad refuses
    // every later load while Busy, so a throw anywhere between "Busy = true" and the guarding
    // try disables the page for the life of the process. The real subscriber here is the
    // UninstallerView, whose Render writes live XAML and rejects a cross-thread call.
    private static async Task BusyFaultSafety(Action<string, bool> check)
    {
        static async Task<Exception?> Capture(Func<Task> action)
        { try { await action().WaitAsync(TimeSpan.FromSeconds(10)); return null; } catch (Exception e) { return e; } }

        // A faulting subscriber on the load path, then a retry that must really re-read.
        await using (var o = InventoryTestOrchestrator.Create())
        {
            var p = new Applications();
            var log = new Diagnostics();
            using var vm = await LoadedUninstaller(p, o, log);
            var raised = 0;
            vm.PropertyChanged += (_, _) => { if (Interlocked.Increment(ref raised) == 1) throw new InvalidOperationException("SECRET view fault"); };
            var escaped = await Capture(() => vm.InitializeAsync());
            check("FIX7 a faulting load subscriber is contained by the view model", escaped is null);
            check("FIX7 a faulting load subscriber leaves Busy clear", !vm.Busy);
            // Expire the entry so the retry must really re-read. Without this the orchestrator
            // correctly serves the retained value from its fresh entry and the check would pass
            // on a load that never happened.
            o.Publish(DataDataset.InstalledApplications, p.Value, TimeSpan.Zero);
            var again = await Capture(() => vm.InitializeAsync());
            check("FIX7 the page still loads after a subscriber fault", again is null && p.Calls == 2 && vm.Rows.Count == 1);
        }

        // Same guard on the confirmed-uninstall path, which mints its token before its try.
        await using (var o = InventoryTestOrchestrator.Create())
        {
            var p = new Applications();
            var log = new Diagnostics();
            using var vm = await LoadedUninstaller(p, o, log);
            var raised = 0;
            vm.PropertyChanged += (_, _) => { if (Interlocked.Increment(ref raised) == 1) throw new InvalidOperationException("SECRET view fault"); };
            var escaped = await Capture(() => vm.UninstallConfirmedAsync(p.Item.Id));
            check("FIX7 a faulting uninstall subscriber is contained by the view model", escaped is null);
            check("FIX7 a faulting uninstall subscriber leaves Busy clear", !vm.Busy);
        }
    }

    // Loads the inventory and selects the fixture, so the confirmed-deletion path is actually
    // reachable. Without this every classification below would be vacuous: the guard at the top
    // of UninstallConfirmedAsync would reject the call before the executor was ever reached.
    private static async Task<UninstallerViewModel> LoadedUninstaller(Applications p, IApplicationDataOrchestrator o, Diagnostics log)
    {
        p.Release.TrySetResult();
        var vm = new UninstallerViewModel(new(new Settings()), p, p, p, p, o, log);
        await vm.InitializeAsync().WaitAsync(TimeSpan.FromSeconds(10));
        vm.SelectedId = p.Item.Id;
        return vm;
    }
    class Probe
    {
        public int Calls; public bool Fail; public Exception? Fault;
        public TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Block() { Started = new(TaskCreationOptions.RunContinuationsAsynchronously); Release = new(TaskCreationOptions.RunContinuationsAsynchronously); }
        protected async Task Read(CancellationToken token)
        { Interlocked.Increment(ref Calls); if (Fail) throw new IOException("SECRET path command line");
          if (Fault is { } fault) throw fault; Started.TrySetResult(); await Release.Task.WaitAsync(token); }
    }
    sealed class Tools : Probe, IWindowsToolsService
    {
        public IReadOnlyList<WindowsToolItem> Value = [new(WindowsToolId.TaskManager, ToolGroup.System, "", ToolLaunchType.SystemExecutable, "fixture", [], false, ToolAvailability.Available)];
        public async Task<IReadOnlyList<WindowsToolItem>> ReadAsync(CancellationToken token)
        { await Read(token); return Value; }
        public Task<ToolLaunchResult> OpenAsync(WindowsToolId id, CancellationToken token) => throw new InvalidOperationException("No automatic actions");
    }
    sealed class Startup : Probe, IStartupInventoryService, IStartupMutationService
    {
        public int Mutations; public bool CancelMutation;
        public StartupItem Item = new("startup-fixture", "Fixture", "Fixture", StartupSourceType.RegistryRun, "fixture", "fixture", "fixture.exe", "", StartupStatus.Enabled,
            StartupScope.CurrentUser, false, false, StartupProtection.None, "", "fixture", "fixture") { CanManage = true };
        public StartupInventorySession Value => new(Guid.NewGuid(), DateTimeOffset.UtcNow, new([Item], true, []));
        public StartupSummary? Summary => null;
        public event EventHandler? Changed { add { } remove { } }
        public async Task<StartupInventorySession> RefreshAsync(CancellationToken token) { await Read(token); return Value; }
        public Task<StartupOperationResult> ApplyAsync(StartupOperationRequest request, CancellationToken token)
        { if (CancelMutation) throw new OperationCanceledException(); Mutations++; Item = Item with { Status = StartupStatus.DisabledBySystemCore }; return Task.FromResult(new StartupOperationResult(StartupOutcome.Success)); }
    }
    sealed class Applications : Probe, IUninstallExecutor, ILeftoverScanner, ILeftoverDeletionService, IGameLauncher
    {
        public int Scans, Deletions; private bool removed;
        public UninstallState Outcome = UninstallState.Removed;
        // Injected so a test can drive the exact failure or cancellation shape the classification
        // is about. Left null, the deterministic defaults below apply.
        public Func<CancellationToken, Task<UninstallOutcome>>? Uninstall;
        public Func<CancellationToken, Task<LeftoverScanSession>>? Scan;
        public Func<CancellationToken, Task<LeftoverDeletionResult>>? Delete;
        public InstalledApplication Item = new("inventory-fixture", "Fixture", "Fixture", "1", null, null, null, null, ApplicationType.Win32, "fixture.exe", false,
            null, null, null, new(InventoryHive.CurrentUser, InventoryView.Registry64, "fixture"), null, false, false, false, true, null);
        public LeftoverItem Leftover = new(Guid.NewGuid(), "inventory-fixture", LeftoverType.File, "fixture", "", LeftoverConfidence.Safe, "", 1, false, false, "");
        public ApplicationInventorySession Value => new(Guid.NewGuid(), DateTimeOffset.UtcNow, new(removed ? [] : [Item], true, []));
        public async Task<ApplicationInventorySession> RefreshAsync(CancellationToken token) { await Read(token); return Value; }
        public Task<UninstallOutcome> UninstallAsync(UninstallRequest request, IProgress<UninstallState>? progress, CancellationToken token)
        { removed = Outcome == UninstallState.Removed;
          return Uninstall is { } injected ? injected(token) : Task.FromResult(new UninstallOutcome(Outcome, Item.Id, 0, null, removed ? new(Guid.NewGuid(), Item.Id, Item.DisplayName, DateTimeOffset.UtcNow) : null)); }
        public Task<LeftoverScanSession> ScanAsync(Guid id, CancellationToken token)
        { Scans++; return Scan is { } injected ? injected(token) : Task.FromResult(new LeftoverScanSession(Guid.NewGuid(), id, Item.Id, DateTimeOffset.UtcNow, [Leftover], true)); }
        public Task<LeftoverDeletionResult> DeleteAsync(LeftoverDeletionRequest request, CancellationToken token)
        { Deletions++; return Delete is { } injected ? injected(token) : Task.FromResult(new LeftoverDeletionResult([], 0)); }
        public bool CanOpen(Source source) => false;
        public Task<bool> OpenAsync(Source source, CancellationToken token) => throw new InvalidOperationException("No automatic launch");
    }
    sealed class Information : Probe, ISystemInformationService
    {
        public SystemInformationSnapshot Value = new(DateTimeOffset.UtcNow,
            new(new("Fixture", "Fixture", "1", "1", "x64", "Fixture Device", null, null, null), CollectionStatus.Ready),
            new([], CollectionStatus.Ready), new([], CollectionStatus.Ready), new([], CollectionStatus.Ready),
            new(new([], []), CollectionStatus.Ready), new(new([], [], false), CollectionStatus.Ready), new([], CollectionStatus.Ready));
        public async Task<SystemInformationSnapshot> CollectAsync(CancellationToken token) { await Read(token); return Value; }
    }
    sealed class Settings(string language = "en") : IUserSettingsService
    {
        public string GetLanguage() => language;
        public void SetLanguage(string language) { }
        public UserTheme GetTheme() => UserTheme.System;
        public void SetTheme(UserTheme theme) { }
    }
    public sealed class Diagnostics : IAppDiagnostics
    {
        // Records are captured so a test can prove an unexpected failure was reported
        // rather than swallowed. A no-op double would make that assertion vacuous.
        public readonly List<(DiagnosticFeature Feature, DiagnosticEvent Action, int Code)> Recorded = [];
        // Signalled rather than polled, so a test waits for the record itself instead of
        // racing a fixed delay against thread-pool scheduling.
        private readonly TaskCompletionSource failed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task FirstProviderFailure => failed.Task;
        public void Record(DiagnosticFeature feature, DiagnosticEvent action, int code = 0)
        {
            lock (Recorded) Recorded.Add((feature, action, code));
            if (action == DiagnosticEvent.ProviderFailure) failed.TrySetResult();
        }
        public int ProviderFailures { get { lock (Recorded) return Recorded.Count(r => r.Action == DiagnosticEvent.ProviderFailure); } }
        public Task FlushAsync(CancellationToken token = default) => Task.CompletedTask;
        public DiagnosticsStatus GetStatus() => new(true, 0, 0, 0);
        public Task<DiagnosticOperation> ExportAsync(CancellationToken token) => throw new NotSupportedException();
        public Task<DiagnosticOperation> ClearOldAsync(CancellationToken token) => throw new NotSupportedException();
        public Task<bool> OpenFolderAsync(DiagnosticFolder folder, CancellationToken token) => throw new NotSupportedException();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
