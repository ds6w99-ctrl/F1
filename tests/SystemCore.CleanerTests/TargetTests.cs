using SystemCore.Core.Cleaner;
using SystemCore.Infrastructure.Cleaner;

internal static class TargetTests
{
    public static async Task<int> Run()
    {
        var ok = true;
        void Check(string name, bool value) { Console.WriteLine($"{(value ? "PASS" : "FAIL")}: {name}"); ok &= value; }
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        CleanerTarget T(CleanerCategory category, string root, string pattern = "*", bool recurse = true, TimeSpan? age = null,
            bool selected = true, bool elevated = false, bool review = false, bool canDeleteDirectories = false) =>
            new(category, root, pattern, recurse, age ?? TimeSpan.FromHours(1), selected, elevated, review, canDeleteDirectories);
        var edge = Path.Combine(local, "Microsoft", "Edge", "User Data");
        var chrome = Path.Combine(local, "Google", "Chrome", "User Data");
        var firefox = Path.Combine(local, "Mozilla", "Firefox", "Profiles");
        var wer = Path.Combine(local, "Microsoft", "Windows", "WER");
        Check("edge cache V1 policy rejected", !WindowsCleanerTargets.IsAllowed(T(CleanerCategory.EdgeCache, Path.Combine(edge, "Default", "Cache"))));
        Check("edge code cache V1 policy rejected", !WindowsCleanerTargets.IsAllowed(T(CleanerCategory.EdgeCache, Path.Combine(edge, "Profile 1", "Code Cache"))));
        Check("edge service worker cache V1 policy rejected", !WindowsCleanerTargets.IsAllowed(T(CleanerCategory.EdgeCache, Path.Combine(edge, "Default", "Service Worker", "CacheStorage"))));
        Check("edge profile data rejected", !WindowsCleanerTargets.IsAllowed(T(CleanerCategory.EdgeCache, Path.Combine(edge, "Default", "Login Data"))));
        Check("edge user data root rejected", !WindowsCleanerTargets.IsAllowed(T(CleanerCategory.EdgeCache, edge)));
        Check("edge outside user data rejected", !WindowsCleanerTargets.IsAllowed(T(CleanerCategory.EdgeCache, Path.Combine(local, "Microsoft", "Edge", "Default", "Cache"))));
        Check("chrome cache V1 policy rejected", !WindowsCleanerTargets.IsAllowed(T(CleanerCategory.ChromeCache, Path.Combine(chrome, "Default", "Cache"))));
        Check("edge root under chrome rejected", !WindowsCleanerTargets.IsAllowed(T(CleanerCategory.ChromeCache, Path.Combine(edge, "Default", "Cache"))));
        Check("firefox cache2 V1 policy rejected", !WindowsCleanerTargets.IsAllowed(T(CleanerCategory.FirefoxCache, Path.Combine(firefox, "abc.default", "cache2"))));
        Check("firefox profile root rejected", !WindowsCleanerTargets.IsAllowed(T(CleanerCategory.FirefoxCache, Path.Combine(firefox, "abc.default"))));
        Check("firefox unknown leaf rejected", !WindowsCleanerTargets.IsAllowed(T(CleanerCategory.FirefoxCache, Path.Combine(firefox, "abc.default", "bookmarkbackups"))));
        Check("wer queue V1 policy rejected", !WindowsCleanerTargets.IsAllowed(T(CleanerCategory.UserWer, Path.Combine(wer, "ReportQueue"))));
        Check("wer root rejected", !WindowsCleanerTargets.IsAllowed(T(CleanerCategory.UserWer, wer)));
        Check("wer unknown leaf rejected", !WindowsCleanerTargets.IsAllowed(T(CleanerCategory.UserWer, Path.Combine(wer, "Custom"))));
        Check("age below floor rejected", !WindowsCleanerTargets.IsAllowed(T(CleanerCategory.EdgeCache, Path.Combine(edge, "Default", "Cache"), age: TimeSpan.FromMinutes(30))));
        Check("age at floor still rejected by V1 policy", !WindowsCleanerTargets.IsAllowed(T(CleanerCategory.EdgeCache, Path.Combine(edge, "Default", "Cache"), age: TimeSpan.FromHours(1))));
        Check("directory deletion rejected", !WindowsCleanerTargets.IsAllowed(T(CleanerCategory.EdgeCache, Path.Combine(edge, "Default", "Cache"), canDeleteDirectories: true)));
        Check("unexpected elevation rejected", !WindowsCleanerTargets.IsAllowed(T(CleanerCategory.EdgeCache, Path.Combine(edge, "Default", "Cache"), elevated: true)));
        Check("wrong pattern rejected", !WindowsCleanerTargets.IsAllowed(T(CleanerCategory.EdgeCache, Path.Combine(edge, "Default", "Cache"), pattern: "*.log")));
        var resolved = new WindowsCleanerTargets().Resolve();
        Check("resolved targets all allowed", resolved.All(WindowsCleanerTargets.IsAllowed));
        Check("V1 policy: resolved contains no unsupported categories", resolved.All(t => t.Category is not CleanerCategory.EdgeCache and not CleanerCategory.ChromeCache and not CleanerCategory.FirefoxCache and not CleanerCategory.UserWer));
        Check("V1 policy: supported UserTemp still resolves", resolved.Any(t => t.Category == CleanerCategory.UserTemp));
        Check("V1 policy: unsupported categories never elevated because absent", !resolved.Any(t => t.Category is CleanerCategory.EdgeCache or CleanerCategory.ChromeCache or CleanerCategory.FirefoxCache or CleanerCategory.UserWer));
        var root = Path.Combine(local, "Temp", "SystemCore-Targets-" + Guid.NewGuid().ToString("N"));
        var left = Path.Combine(root, "left");
        var right = Path.Combine(root, "right");
        Directory.CreateDirectory(left); Directory.CreateDirectory(right);
        foreach (var file in new[] { Path.Combine(left, "one.tmp"), Path.Combine(right, "two.tmp") })
        { File.WriteAllText(file, "fixture"); File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddDays(-2)); }
        var session = await new WindowsCleanerScanner(new TwoRoots(left, right)).ScanAsync(default);
        var category = session.Categories.Single(c => c.Category == CleanerCategory.UserTemp);
        Check("multi-root aggregates both roots", category.Status == CleanerScanStatus.Complete && category.Items.Count == 2 && category.Bytes == 14);
        Check("multi-root items keep distinct roots", category.Items.Select(i => i.DisplayPath).Distinct().Count() == 2);
        var nested = Path.Combine(left, "nested"); Directory.CreateDirectory(nested);
        var nestedFile = Path.Combine(nested, "deep.tmp"); File.WriteAllText(nestedFile, "fixture"); File.SetLastWriteTimeUtc(nestedFile, DateTime.UtcNow.AddDays(-2));
        var deduped = await new WindowsCleanerScanner(new NestedRoots(left, nested)).ScanAsync(default);
        var dedupedCategory = deduped.Categories.Single(c => c.Category == CleanerCategory.UserTemp);
        Check("nested root deduplicated under accepted ancestor", dedupedCategory.Items.Count == 2 && dedupedCategory.Items.Count(i => i.DisplayPath == nestedFile) == 1);
        try { Directory.Delete(root, recursive: true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return ok ? 0 : 1;
    }
    private sealed class TwoRoots(string left, string right) : ICleanerTargetProvider
    {
        public IReadOnlyList<CleanerTarget> Resolve() =>
        [
            new(CleanerCategory.UserTemp, left, "*", true, TimeSpan.FromHours(24), true, false, false),
            new(CleanerCategory.UserTemp, right, "*", true, TimeSpan.FromHours(24), true, false, false),
        ];
    }
    private sealed class NestedRoots(string parent, string child) : ICleanerTargetProvider
    {
        public IReadOnlyList<CleanerTarget> Resolve() =>
        [
            new(CleanerCategory.UserTemp, child, "*", true, TimeSpan.FromHours(24), true, false, false),
            new(CleanerCategory.UserTemp, parent, "*", true, TimeSpan.FromHours(24), true, false, false),
        ];
    }
}
