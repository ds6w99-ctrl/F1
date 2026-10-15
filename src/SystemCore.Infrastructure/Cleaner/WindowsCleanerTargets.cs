using SystemCore.Core.Cleaner;

namespace SystemCore.Infrastructure.Cleaner;

public sealed class WindowsCleanerTargets : ICleanerTargetProvider
{
    // No target may be configured to delete anything newer than this floor. Browser caches
    // churn within minutes, so caches use the floor while system/temp roots stay conservative.
    internal static readonly TimeSpan MinimumAgeFloor = TimeSpan.FromHours(1);
    private static readonly TimeSpan ConservativeAge = TimeSpan.FromHours(24);
    private static readonly TimeSpan CacheAge = MinimumAgeFloor;
    // Chromium cache directories, expressed relative to a single browser profile directory.
    private static readonly string[][] ChromiumCacheLeaves =
    [
        ["Cache"], ["Code Cache"], ["GPUCache"], ["DawnGraphiteCache"], ["DawnWebGPUCache"],
        ["GrShaderCache"], ["ShaderCache"], ["Media Cache"], ["Service Worker", "CacheStorage"], ["Service Worker", "ScriptCache"],
    ];
    private static readonly string[] FirefoxCacheLeaves = ["cache2", "startupCache", "shader-cache"];
    private static readonly string[] WerLeaves = ["ReportQueue", "ReportArchive", "Temp"];
    private static string Local => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    private static string Windows => Environment.GetFolderPath(Environment.SpecialFolder.Windows);
    private static string UserTempRoot => Path.Combine(Local, "Temp");
    private static string EdgeUserData => Path.Combine(Local, "Microsoft", "Edge", "User Data");
    private static string ChromeUserData => Path.Combine(Local, "Google", "Chrome", "User Data");
    private static string FirefoxProfiles => Path.Combine(Local, "Mozilla", "Firefox", "Profiles");
    private static string WerRoot => Path.Combine(Local, "Microsoft", "Windows", "WER");
    // A deliberately small, structural allowlist. The elevated helper and the service both
    // re-check every target here so an injected provider cannot widen the delete surface.
    internal static bool IsAllowed(CleanerTarget target)
    {
        if (target.CanDeleteDirectories || target.MinimumAge < MinimumAgeFloor) return false;
        if (target.Category != CleanerCategory.WindowsTemp && IsProtected(target.Root)) return false;
        return target.Category switch
        {
            // A narrower UserTemp root is useful for isolated tests and cannot expand the allowlist.
            CleanerCategory.UserTemp => UnderOrEqual(UserTempRoot, target.Root, child: true) && Simple(target, "*", recurse: true, elevated: false),
            CleanerCategory.WindowsTemp => UnderOrEqual(Path.Combine(Windows, "Temp"), target.Root, child: false) && Simple(target, "*", recurse: true, elevated: true),
            CleanerCategory.ShaderCache => UnderOrEqual(Path.Combine(Local, "D3DSCache"), target.Root, child: false) && Simple(target, "*", recurse: true, elevated: false),
            CleanerCategory.CrashDumps => UnderOrEqual(Path.Combine(Local, "CrashDumps"), target.Root, child: false) && Simple(target, "*.dmp", recurse: false, elevated: false),
            CleanerCategory.Thumbnails => UnderOrEqual(Path.Combine(Local, "Microsoft", "Windows", "Explorer"), target.Root, child: false) && Simple(target, "thumbcache_*.db", recurse: false, elevated: false),
            CleanerCategory.EdgeCache => ChromiumTarget(EdgeUserData, target.Root) && Simple(target, "*", recurse: true, elevated: false),
            CleanerCategory.ChromeCache => ChromiumTarget(ChromeUserData, target.Root) && Simple(target, "*", recurse: true, elevated: false),
            CleanerCategory.FirefoxCache => LeafUnder(FirefoxProfiles, target.Root, FirefoxCacheLeaves, levels: 2) && Simple(target, "*", recurse: true, elevated: false),
            CleanerCategory.UserWer => LeafUnder(WerRoot, target.Root, WerLeaves, levels: 1) && Simple(target, "*", recurse: true, elevated: false),
            _ => false,
        };
    }
    public IReadOnlyList<CleanerTarget> Resolve()
    {
        var targets = new List<CleanerTarget>();
        void Add(CleanerCategory category, string root, string pattern, bool recurse, TimeSpan age, bool selected, bool review = false, bool elevated = false)
        {
            if (CleanerSafety.Canonical(root) is not null)
                targets.Add(new(category, root, pattern, recurse, age, selected, elevated, review));
        }
        // Environment TEMP can be redirected to arbitrary personal/protected data. Only accept
        // the exact conventional known-folder Temp root, never blindly trust the environment.
        if (string.Equals(CleanerSafety.Canonical(Path.GetTempPath()), CleanerSafety.Canonical(UserTempRoot), StringComparison.OrdinalIgnoreCase))
            Add(CleanerCategory.UserTemp, UserTempRoot, "*", true, ConservativeAge, true);
        Add(CleanerCategory.WindowsTemp, Path.Combine(Windows, "Temp"), "*", true, ConservativeAge, false, elevated: true);
        Add(CleanerCategory.ShaderCache, Path.Combine(Local, "D3DSCache"), "*", true, ConservativeAge, true);
        Add(CleanerCategory.CrashDumps, Path.Combine(Local, "CrashDumps"), "*.dmp", false, ConservativeAge, false, review: true);
        Add(CleanerCategory.Thumbnails, Path.Combine(Local, "Microsoft", "Windows", "Explorer"), "thumbcache_*.db", false, ConservativeAge, false, review: true);
        AddChromium(CleanerCategory.EdgeCache, EdgeUserData);
        AddChromium(CleanerCategory.ChromeCache, ChromeUserData);
        AddDirectoryLeaves(CleanerCategory.FirefoxCache, FirefoxProfiles, FirefoxCacheLeaves);
        foreach (var leaf in WerLeaves)
        {
            var root = Path.Combine(WerRoot, leaf);
            if (Directory.Exists(root)) Add(CleanerCategory.UserWer, root, "*", true, ConservativeAge, true);
        }
        return targets.AsReadOnly();
        void AddChromium(CleanerCategory category, string userData)
        {
            if (!Directory.Exists(userData)) return;
            foreach (var profile in SafeDirectories(userData))
                foreach (var leaf in ChromiumCacheLeaves)
                {
                    var root = profile;
                    foreach (var segment in leaf) root = Path.Combine(root, segment);
                    if (Directory.Exists(root)) Add(category, root, "*", true, CacheAge, true);
                }
        }
        void AddDirectoryLeaves(CleanerCategory category, string parent, string[] leaves)
        {
            if (!Directory.Exists(parent)) return;
            foreach (var directory in SafeDirectories(parent))
                foreach (var leaf in leaves)
                {
                    var root = Path.Combine(directory, leaf);
                    if (Directory.Exists(root)) Add(category, root, "*", true, CacheAge, true);
                }
        }
    }
    private static IEnumerable<string> SafeDirectories(string root)
    {
        try { return Directory.EnumerateDirectories(root); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return []; }
    }
    private static bool Simple(CleanerTarget target, string pattern, bool recurse, bool elevated) =>
        target.Pattern == pattern && target.Recurse == recurse && target.RequiresElevation == elevated;
    private static bool UnderOrEqual(string expected, string root, bool child)
    {
        if (string.Equals(CleanerSafety.Canonical(expected), CleanerSafety.Canonical(root), StringComparison.OrdinalIgnoreCase)) return true;
        return child && CleanerSafety.IsChild(expected, root);
    }
    // root must be <userData>\<profile>\<leaf...> with no extra segments and no wildcard leaf.
    private static bool ChromiumTarget(string userData, string root)
    {
        if (!CleanerSafety.IsChild(userData, root)) return false;
        var parts = Relative(userData, root);
        return ChromiumCacheLeaves.Any(leaf => parts.Length == leaf.Length + 1 &&
            leaf.Select((segment, i) => string.Equals(parts[i + 1], segment, StringComparison.OrdinalIgnoreCase)).All(match => match));
    }
    // root must be <parent>\<name>\<leaf...> with an exact segment count and a known leaf.
    private static bool LeafUnder(string parent, string root, string[] leaves, int levels)
    {
        if (!CleanerSafety.IsChild(parent, root)) return false;
        var parts = Relative(parent, root);
        return parts.Length == levels && leaves.Contains(parts[^1], StringComparer.OrdinalIgnoreCase);
    }
    private static string[] Relative(string parent, string root) => Path.GetRelativePath(parent, root).Split('\\');
    private static bool IsProtected(string root) => new[] { Windows,
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        Path.Combine(Path.GetPathRoot(Windows)!, "System Volume Information"),
        Path.Combine(Path.GetPathRoot(Windows)!, "Boot"),
        Path.Combine(Path.GetPathRoot(Windows)!, "EFI") }
        .Any(protectedRoot => string.Equals(CleanerSafety.Canonical(protectedRoot), CleanerSafety.Canonical(root), StringComparison.OrdinalIgnoreCase) ||
            CleanerSafety.IsChild(protectedRoot, root));
}
