using SystemCore.Core.Cleaner;

internal static class CleanerSafetyTests
{
    public static bool Run()
    {
        var ok = true;
        void Check(string name, bool passed) { Console.WriteLine($"{(passed ? "PASS" : "FAIL")}: Cleaner {name}"); ok &= passed; }
        const string root = @"C:\Users\Test\AppData\Local\Temp";
        Check("valid child", CleanerSafety.IsChild(root, root + @"\old.tmp"));
        Check("root itself rejected", !CleanerSafety.IsChild(root, root));
        Check("similar prefix rejected", !CleanerSafety.IsChild(root, root + @"Evil\old.tmp"));
        Check("case insensitive boundary", CleanerSafety.IsChild(root.ToUpperInvariant(), root.ToLowerInvariant() + @"\a.tmp"));
        Check("trailing root separator", CleanerSafety.IsChild(root + "\\", root + @"\a.tmp"));
        Check("separator normalization", CleanerSafety.IsChild(root, root.Replace('\\', '/') + "/a.tmp"));
        foreach (var path in new[] { "", " ", "a.tmp", @"..\Windows", @"C:\Temp\..\Windows", @"C:\a\b\..\..\Windows",
            @"\\server\share\a", @"\\?\C:\Temp\a", @"\\.\C:\Temp\a", @"C:\Temp:file", "C:", "\\", @"C:\", @"C:\a?b",
            @"C:\a\b.", @"C:\a\b ", @"C:\a\CON", @"C:\a\LPT1.txt", "C:\\a\u0001b", "C:\\" + new string('a', 241) })
            Check("reject invalid path " + Array.IndexOf(new[] { path }, path), CleanerSafety.Canonical(path) is null);
        foreach (var path in new[] { @"C:\Windows", @"C:\Windows\System32\a", @"C:\Program Files\a", @"C:\Users\Test\AppData\Local", @"C:\Other\a" })
            Check("outside root rejected", !CleanerSafety.IsChild(root, path));
        Check("reparse directory metadata", !CleanerSafety.SafeFileAttributes(0x410, 1));
        Check("reparse file metadata", !CleanerSafety.SafeFileAttributes(0x400, 1));
        Check("junction metadata", !CleanerSafety.SafeFileAttributes(0x400 | 0x10, 1));
        Check("symlink metadata", !CleanerSafety.SafeFileAttributes(0x400 | 0x20, 1));
        Check("hard links rejected", !CleanerSafety.SafeFileAttributes(0x20, 2));
        Check("regular file accepted", CleanerSafety.SafeFileAttributes(0x20, 1));
        Check("directory not a file", !CleanerSafety.SafeFileAttributes(0x10, 1));
        Check("sparse rejected", !CleanerSafety.SafeFileAttributes(0x200, 1));
        var now = DateTimeOffset.UtcNow;
        Check("old file eligible", CleanerSafety.Eligible(10, now.AddDays(-2).UtcDateTime.ToFileTimeUtc(), now, TimeSpan.FromHours(24)));
        Check("recent file rejected", !CleanerSafety.Eligible(10, now.UtcDateTime.ToFileTimeUtc(), now, TimeSpan.FromHours(24)));
        Check("future time rejected", !CleanerSafety.Eligible(10, now.AddDays(1).UtcDateTime.ToFileTimeUtc(), now, TimeSpan.FromHours(24)));
        Check("unknown time rejected", !CleanerSafety.Eligible(10, 0, now, TimeSpan.Zero));
        Check("bad size rejected", !CleanerSafety.Eligible(-1, now.AddDays(-2).UtcDateTime.ToFileTimeUtc(), now, TimeSpan.Zero));
        Check("64-bit size addition", CleanerSafety.TryAddSize(3000000000, 3000000000, out var total) && total == 6000000000);
        Check("size overflow rejected", !CleanerSafety.TryAddSize(long.MaxValue, 1, out _));
        var state = new CleanerScanState();
        var weak = PublishGraph(state);
        // Isolated lifetime regression, not runtime behavior or a process-memory test.
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Check("Dashboard summary releases full scan graph", !weak.Session.IsAlive && !weak.Item.IsAlive);
        Check("Dashboard summary preserves total and status", state.Latest is { Complete: true, Bytes: 7 });
        state.Set(null);
        Check("Dashboard reset clears summary", state.Latest is null);
        return ok;
    }
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static (WeakReference Session, WeakReference Item) PublishGraph(CleanerScanState state)
    {
        var item = new CleanerItem(Guid.NewGuid(), CleanerCategory.UserTemp, "fixture.tmp", @"C:\fixture.tmp", 7, 1, new(1, 1, 7, 1, 1, 1));
        var scan = new CleanerScanSession(Guid.NewGuid(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
            [new(CleanerCategory.UserTemp, CleanerScanStatus.Complete, [item], 7, 1, new Dictionary<CleanerSkip, long>(), true, false, false)]);
        state.Set(scan);
        return (new(scan), new(item));
    }
}
