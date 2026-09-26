using SystemCore.Core.Cleaner;
using SystemCore.Infrastructure.Cleaner;

internal static class ExecutorTests
{
    public static async Task<int> Run()
    {
        var ok = true;
        void Check(string name, bool value) { Console.WriteLine($"{(value ? "PASS" : "FAIL")}: {name}"); ok &= value; }
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp", "SystemCore-Cleaner-Test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string Make(string name, string text = "fixture")
        {
            var path = Path.Combine(root, name);
            File.WriteAllText(path, text); File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-2)); return path;
        }
        var chosen = Make("chosen.tmp"); var untouched = Make("unselected.tmp");
        var changed = Make("changed.tmp"); var locked = Make("locked.tmp"); var recent = Make("recent.tmp");
        File.SetLastWriteTimeUtc(recent, DateTime.UtcNow);
        var targets = new FixtureTargets(root);
        var service = new WindowsCleanerService(targets);
        var scan = await service.ScanAsync(default);
        var items = scan.Categories.Single(c => c.Category == CleanerCategory.UserTemp).Items;
        Guid Id(string name) => items.Single(i => i.RelativePath == name).Id;
        File.AppendAllText(changed, "changed-since-scan");
        using (var hold = File.Open(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var result = await service.CleanAsync(new(scan.Id, [Id("chosen.tmp"), Id("changed.tmp"), Id("locked.tmp")]), default);
            Check("selected fixture deleted by validated handle", result.Accepted && result.Deleted == 1 && !File.Exists(chosen));
            Check("actual removed bytes exclude skipped files", result.RemovedBytes == 7);
            Check("changed file preserved", File.Exists(changed) && result.Items.Any(i => i.Outcome == CleanerItemOutcome.Changed));
            Check("locked file preserved", File.Exists(locked) && result.Items.Any(i => i.Outcome == CleanerItemOutcome.InUse));
        }
        Check("unselected and recent files preserved", File.Exists(untouched) && File.Exists(recent));
        Check("root survived", Directory.Exists(root));
        Check("consumed session rejected", !(await service.CleanAsync(new(scan.Id, [Id("unselected.tmp")]), default)).Accepted);
        async Task<(CleanerScanSession Scan, Guid Id)> Fresh()
        {
            var s = await service.ScanAsync(default);
            return (s, s.Categories.Single(c => c.Category == CleanerCategory.UserTemp).Items.Single(i => i.RelativePath == "unselected.tmp").Id);
        }
        var f = await Fresh();
        Check("unknown session rejected", !(await service.CleanAsync(new(Guid.NewGuid(), [f.Id]), default)).Accepted);
        f = await Fresh();
        Check("unknown item rejected atomically", !(await service.CleanAsync(new(f.Scan.Id, [f.Id, Guid.NewGuid()]), default)).Accepted && File.Exists(untouched));
        f = await Fresh();
        Check("duplicate IDs rejected", !(await service.CleanAsync(new(f.Scan.Id, [f.Id, f.Id]), default)).Accepted);
        f = await Fresh(); service.Invalidate();
        Check("invalidated session rejected", !(await service.CleanAsync(new(f.Scan.Id, [f.Id]), default)).Accepted);
        f = await Fresh(); var previous = f; f = await Fresh();
        Check("old scan replaced by new scan", !(await service.CleanAsync(new(previous.Scan.Id, [previous.Id]), default)).Accepted);
        f = await Fresh();
        using (var cancel = new CancellationTokenSource())
        {
            cancel.Cancel(); var result = await service.CleanAsync(new(f.Scan.Id, [f.Id]), cancel.Token);
            Check("cancelled clean preserves fixture", result.Deleted == 0 && result.RemovedBytes == 0 && result.Items.Single().Outcome == CleanerItemOutcome.Cancelled && File.Exists(untouched));
        }
        f = await Fresh();
        // The publicly returned record can be copied, but it is not accepted by CleanAsync.
        var forged = f.Scan with { Id = Guid.NewGuid() };
        Check("caller-modified session not trusted", !(await service.CleanAsync(new(forged.Id, [f.Id]), default)).Accepted);
        f = await Fresh();
        var moved = Path.Combine(root, "original-kept.tmp"); File.Move(untouched, moved);
        Make("unselected.tmp");
        var replacement = await service.CleanAsync(new(f.Scan.Id, [f.Id]), default);
        Check("replacement with same name preserved", replacement.Deleted == 0 && replacement.Items.Single().Outcome == CleanerItemOutcome.Changed && File.Exists(untouched));
        f = await Fresh(); File.Move(untouched, Path.Combine(root, "gone-but-kept.tmp"));
        var gone = await service.CleanAsync(new(f.Scan.Id, [f.Id]), default);
        Check("already-gone item counts zero", gone.RemovedBytes == 0 && gone.Items.Single().Outcome == CleanerItemOutcome.AlreadyGone);
        var outside = root + "-outside"; Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "sentinel.tmp"), "outside");
        var child = Path.Combine(root, "child"); Directory.CreateDirectory(child);
        var childFile = Path.Combine(child, "old.tmp"); File.WriteAllText(childFile, "fixture"); File.SetLastWriteTimeUtc(childFile, DateTime.UtcNow.AddDays(-2));
        var childScan = await service.ScanAsync(default);
        var childId = childScan.Categories.Single(c => c.Category == CleanerCategory.UserTemp).Items.Single(i => i.RelativePath == @"child\old.tmp").Id;
        Directory.Move(child, child + "-kept"); JunctionFixture.Create(child, outside);
        var escaped = await service.CleanAsync(new(childScan.Id, [childId]), default);
        Check("ancestor replaced by junction rejected", escaped.Deleted == 0 && escaped.Items.Single().Outcome == CleanerItemOutcome.SafetyRejected);
        Check("outside root sentinel survived", File.ReadAllText(Path.Combine(outside, "sentinel.tmp")) == "outside");
        var malicious = new WindowsCleanerService(new FixtureTargets(Environment.GetFolderPath(Environment.SpecialFolder.System)));
        var maliciousScan = await malicious.ScanAsync(default);
        Check("protected root cannot issue eligible IDs", maliciousScan.Categories.All(c => c.Items.Count == 0));
        Check("empty request rejected", !(await service.CleanAsync(new(Guid.NewGuid(), []), default)).Accepted);
        Check("oversize request rejected", !(await service.CleanAsync(new(Guid.NewGuid(), Enumerable.Repeat(Guid.NewGuid(), WindowsCleanerScanner.MaximumTotalEntries + 1).ToArray()), default)).Accepted);
        Make("unselected.tmp");
        var clock = new AdjustableTime();
        var expiring = new WindowsCleanerService(targets, clock);
        var expiringScan = await expiring.ScanAsync(default);
        var expiryId = expiringScan.Categories.Single(c => c.Category == CleanerCategory.UserTemp).Items.First().Id;
        clock.Offset = TimeSpan.FromMinutes(11);
        Check("expired session rejected", !(await expiring.CleanAsync(new(expiringScan.Id, [expiryId]), default)).Accepted);
        Console.WriteLine("Synthetic execution only; surviving fixture directories retained.");
        return ok ? 0 : 1;
    }
    private sealed class AdjustableTime : TimeProvider
    {
        public TimeSpan Offset { get; set; }
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow + Offset;
    }
}
