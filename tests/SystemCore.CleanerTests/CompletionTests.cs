using System.IO.Pipes;
using System.Text;
using SystemCore.Core.Cleaner;
using SystemCore.Infrastructure.Cleaner;

internal static class CompletionTests
{
    public static async Task<int> Run()
    {
        var ok = true;
        void Check(string name, bool passed) { Console.WriteLine($"{(passed ? "PASS" : "FAIL")}: {name}"); ok &= passed; }
        var fake = new FakeBin(); var bin = new WindowsRecycleBinService(fake);
        var snapshot = await bin.QueryAsync(default);
        Check("Recycle query uses wrapper counts", snapshot == new RecycleBinSnapshot(true, 3, 42) && fake.Empties == 0);
        var service = new WindowsCleanerService(new NoTargets(), recycleBin: bin);
        var scan = await service.ScanAsync(default);
        var category = scan.Categories.Single(c => c.Category == CleanerCategory.RecycleBin);
        Check("Recycle category real wrapper data default unselected", category.ItemCount == 3 && category.Bytes == 42 && !category.DefaultSelected && category.Items.Count == 0);
        var invalid = await service.CleanAsync(new(Guid.NewGuid(), [], true), default);
        Check("Recycle invalid session cannot empty", !invalid.Accepted && fake.Empties == 0);
        scan = await service.ScanAsync(default);
        var noSelection = await service.CleanAsync(new(scan.Id, []), default);
        Check("Recycle requires explicit selection", !noSelection.Accepted && fake.Empties == 0);
        var cleaned = await service.CleanAsync(new(scan.Id, [], true), default);
        Check("Recycle confirmed session calls Shell wrapper", cleaned.Accepted && cleaned.RecycleBin == RecycleBinOutcome.Completed && fake.Empties == 1);
        Check("Recycle estimate never counted as actual bytes", cleaned.RemovedBytes == 0 && cleaned.Deleted == 0);
        Check("Recycle consumed session rejected", !(await service.CleanAsync(new(scan.Id, [], true), default)).Accepted && fake.Empties == 1);
        fake.Snapshot = new(true, 4, 50);
        Check("Recycle changed contents skipped", await bin.EmptyAsync(snapshot, default) == RecycleBinOutcome.Changed && fake.Empties == 1);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Check("Recycle cancellation prevents Shell call", await bin.EmptyAsync(fake.Snapshot, cancellation.Token) == RecycleBinOutcome.Cancelled && fake.Empties == 1);
        fake.Success = false;
        Check("Recycle failure not reported as success", await bin.EmptyAsync(fake.Snapshot, default) == RecycleBinOutcome.Failed);
        fake.Snapshot = new(false, 0, 0);
        Check("Recycle unavailable query skips", await bin.EmptyAsync(snapshot, default) == RecycleBinOutcome.Unavailable);

        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp", "SystemCore-Cleaner-Test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var file = Path.Combine(root, "helper-fixture.tmp"); File.WriteAllText(file, "helper-test"); File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddDays(-2));
        var fixtureScan = await new WindowsCleanerScanner(new FixtureTargets(root)).ScanAsync(default);
        var item = fixtureScan.Categories.Single(c => c.Category == CleanerCategory.UserTemp).Items.Single();
        var nonce = new string('A', 64);
        var request = new ElevatedCleanerRequest(1, nonce, fixtureScan.Id, fixtureScan.Completed,
            [new(item.Id, CleanerCategory.WindowsTemp, item.RelativePath, item.Identity)]);
        bool Valid(ElevatedCleanerRequest r) => CleanerElevationProtocol.Validate(r, nonce, root, DateTimeOffset.UtcNow);
        Check("Helper valid request", Valid(request));
        foreach (var bad in new[] { "..\\outside.tmp", "child\\..\\outside.tmp", "C:\\Windows\\a", "\\\\server\\share\\a", "\\\\?\\C:\\a", "\\\\.\\C:\\a", "a:stream", "", ".", "..", "a. ", "CON" })
            Check("Helper malicious relative path rejected", !Valid(request with { Items = [request.Items[0] with { RelativePath = bad }] }));
        Check("Helper wrong category", !Valid(request with { Items = [request.Items[0] with { Category = CleanerCategory.UserTemp }] }));
        Check("Helper wrong version", !Valid(request with { Version = 2 }));
        Check("Helper nonce mismatch", !Valid(request with { Nonce = new string('B', 64) }));
        Check("Helper expired request", !Valid(request with { ScannedAt = DateTimeOffset.UtcNow.AddMinutes(-11) }));
        Check("Helper missing metadata", !Valid(request with { Items = [request.Items[0] with { Identity = null! }] }));
        Check("Helper duplicate IDs", !Valid(request with { Items = [request.Items[0], request.Items[0]] }));
        Check("Helper item count limit", !Valid(request with { Items = Enumerable.Repeat(request.Items[0], 2001).ToArray() }));
        Check("Helper normal payload fits", CleanerElevationProtocol.FitsPayload(request));
        Check("Helper byte payload limit before elevation", !CleanerElevationProtocol.FitsPayload(request with { Nonce = new string('A', CleanerElevationProtocol.MaximumPayload) }));
        Check("Production scanner practical limits", WindowsCleanerScanner.MaximumCategoryEntries == 200000 && WindowsCleanerScanner.MaximumTotalEntries == 500000);
        async Task<bool> RejectFrame(byte[] bytes)
        {
            try { using var stream = new MemoryStream(bytes); await CleanerElevationProtocol.ReadAsync<ElevatedCleanerRequest>(stream, default); return false; }
            catch (Exception ex) when (ex is IOException or InvalidDataException or System.Text.Json.JsonException) { return true; }
        }
        Check("Helper oversized frame rejected before allocation", await RejectFrame(BitConverter.GetBytes(CleanerElevationProtocol.MaximumPayload + 1)));
        Check("Helper malformed JSON rejected", await RejectFrame([1, 0, 0, 0, (byte)'{']));
        Check("Helper truncated frame rejected", await RejectFrame([10, 0, 0, 0, (byte)'{']));
        Check("Helper null request rejected", !CleanerElevationProtocol.Validate(null, nonce, root, DateTimeOffset.UtcNow));
        var target = new FixtureTargets(root).Resolve().Single();
        var pinnedDirectory = Path.Combine(root, "pinned-empty"); Directory.CreateDirectory(pinnedDirectory);
        var mutationBlocked = false;
        using (var lease = PinnedCleanerPath.Open(pinnedDirectory, true))
        {
            try { JunctionFixture.Create(pinnedDirectory, root + "-outside"); }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 32) { mutationBlocked = true; }
        }
        Check("Pinned ancestor blocks write/reparse mutation", mutationBlocked);
        var pipeName = "SystemCore-Test-" + Guid.NewGuid().ToString("N");
        using var server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        var peer = Task.Run(async () =>
        {
            using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await client.ConnectAsync();
            var received = await CleanerElevationProtocol.ReadAsync<ElevatedCleanerRequest>(client, default);
            var result = CleanerElevationProtocol.ExecuteAtTrustedTarget(received, nonce, target, default);
            await CleanerElevationProtocol.WriteAsync(client, result, default);
        });
        await server.WaitForConnectionAsync();
        await CleanerElevationProtocol.WriteAsync(server, request, default);
        var response = await CleanerElevationProtocol.ReadAsync<ElevatedCleanerResponse>(server, default); await peer;
        Check("Synthetic helper framed pipe flow deletes only fixture", response.Accepted && response.Items.Single().Outcome == CleanerItemOutcome.Deleted && !File.Exists(file));
        Check("Synthetic helper actual bytes", response.Items.Single().RemovedBytes == 11 && Directory.Exists(root));
        File.WriteAllText(file, "replacement"); File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddDays(-2));
        var changed = CleanerElevationProtocol.ExecuteAtTrustedTarget(request, nonce, target, default);
        Check("Helper changed identity preserved", changed.Items.Single().Outcome == CleanerItemOutcome.Changed && File.Exists(file));
        var linked = Path.Combine(root, "link"); JunctionFixture.Create(linked, root + "-outside");
        var reparse = request with { Items = [request.Items[0] with { RelativePath = "link\\fixture.tmp" }] };
        Check("Helper reparse target rejected", CleanerElevationProtocol.ExecuteAtTrustedTarget(reparse, nonce, target, default).Items.Single().Outcome == CleanerItemOutcome.SafetyRejected);
        Check("Helper protected test target refused", !CleanerElevationProtocol.ExecuteAtTrustedTarget(request, nonce, target with { Root = Environment.GetFolderPath(Environment.SpecialFolder.System) }, default).Accepted);
        var deepRoot = Path.Combine(root, "depth-test"); Directory.CreateDirectory(deepRoot);
        var eligiblePath = Path.Combine(deepRoot, "eligible.tmp"); File.WriteAllText(eligiblePath, "keep"); File.SetLastWriteTimeUtc(eligiblePath, DateTime.UtcNow.AddDays(-2));
        var deep = deepRoot;
        for (var i = 0; i <= WindowsCleanerScanner.MaximumDepth; i++) { deep = Path.Combine(deep, "d"); Directory.CreateDirectory(deep); }
        var incompleteService = new WindowsCleanerService(new FixtureTargets(deepRoot));
        var incompleteScan = await incompleteService.ScanAsync(default);
        var incompleteCategory = incompleteScan.Categories.Single(c => c.Category == CleanerCategory.UserTemp);
        Check("Incomplete category retains honest partial preview", incompleteCategory.Status == CleanerScanStatus.Incomplete && incompleteCategory.Items.Count == 1);
        var refused = await incompleteService.CleanAsync(new(incompleteScan.Id, [incompleteCategory.Items[0].Id]), default);
        Check("Incomplete category cannot clean even valid discovered ID", !refused.Accepted && File.ReadAllText(eligiblePath) == "keep");
        var noElevation = new RecordingElevation();
        var ordinaryRoot = Path.Combine(root, "ordinary"); Directory.CreateDirectory(ordinaryRoot);
        var ordinaryPath = Path.Combine(ordinaryRoot, "ordinary.tmp"); File.WriteAllText(ordinaryPath, "fixture"); File.SetLastWriteTimeUtc(ordinaryPath, DateTime.UtcNow.AddDays(-2));
        var ordinary = new WindowsCleanerService(new FixtureTargets(ordinaryRoot), elevated: noElevation);
        var ordinaryScan = await ordinary.ScanAsync(default);
        var ordinaryItem = ordinaryScan.Categories.Single(c => c.Category == CleanerCategory.UserTemp).Items.First();
        var ordinaryResult = await ordinary.CleanAsync(new(ordinaryScan.Id, [ordinaryItem.Id]), default);
        Check("Normal user category never invokes elevation", ordinaryResult.Deleted == 1 && noElevation.Calls == 0);
        File.WriteAllText(ordinaryPath, "fixture"); File.SetLastWriteTimeUtc(ordinaryPath, DateTime.UtcNow.AddDays(-2));
        var failingProvider = new FailingTargets(ordinaryRoot);
        var failingService = new WindowsCleanerService(failingProvider);
        var failScan = await failingService.ScanAsync(default);
        var failItem = failScan.Categories.Single(c => c.Category == CleanerCategory.UserTemp).Items.Single();
        failingProvider.Fail = true;
        var failedResult = await failingService.CleanAsync(new(failScan.Id, [failItem.Id]), default);
        Check("Clean provider failure accounted without deletion", failedResult.Failed == 1 && failedResult.RemovedBytes == 0 && File.Exists(ordinaryPath));
        var unknownResult = new CleanerCleanResult(true, [new(Guid.NewGuid(), CleanerItemOutcome.Unknown, 0)]);
        Check("Unknown helper outcome not counted as skipped or removed", unknownResult.Failed == 1 && unknownResult.Skipped == 0 && unknownResult.RemovedBytes == 0);
        // Retain the deliberately pathological depth fixture outside real Temp so it does
        // not make subsequent real-user scans incomplete. Move only this test-created tree.
        Directory.Move(deepRoot, Path.Combine(AppContext.BaseDirectory, "depth-fixture-" + Guid.NewGuid().ToString("N")));
        Console.WriteLine("All destructive operations used new synthetic files; real Recycle Bin/Windows Temp were not emptied.");
        return ok ? 0 : 1;
    }
    private sealed class FakeBin : IRecycleBinShell
    {
        public RecycleBinSnapshot Snapshot = new(true, 3, 42);
        public int Empties;
        public bool Success = true;
        public RecycleBinSnapshot Query() => Snapshot;
        public bool Empty() { Empties++; return Success; }
    }
    private sealed class NoTargets : ICleanerTargetProvider { public IReadOnlyList<CleanerTarget> Resolve() => []; }
    private sealed class RecordingElevation : IElevatedCleanerClient
    {
        public int Calls;
        public Task<IReadOnlyList<CleanerItemResult>> CleanAsync(Guid session, DateTimeOffset scannedAt, IReadOnlyList<CleanerItem> items, CancellationToken token)
        { Calls++; return Task.FromResult<IReadOnlyList<CleanerItemResult>>(Array.Empty<CleanerItemResult>()); }
    }
    private sealed class FailingTargets(string root) : ICleanerTargetProvider
    {
        public bool Fail;
        public IReadOnlyList<CleanerTarget> Resolve() => Fail ? throw new IOException("Synthetic provider failure") : new FixtureTargets(root).Resolve();
    }
}
