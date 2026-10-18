using SystemCore.Core.Cleaner;

namespace SystemCore.Infrastructure.Cleaner;

public sealed class WindowsCleanerTargets : ICleanerTargetProvider
{
    // No target may be configured to delete anything newer than this floor. System/temp
    // roots use a conservative age; V1 does not ship browser-cache targets (see F1-002).
    internal static readonly TimeSpan MinimumAgeFloor = TimeSpan.FromHours(1);
    private static readonly TimeSpan ConservativeAge = TimeSpan.FromHours(24);
    private static string Local => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    private static string Windows => Environment.GetFolderPath(Environment.SpecialFolder.Windows);
    private static string UserTempRoot => Path.Combine(Local, "Temp");
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
            // V1 policy (F1-002): browser caches and user WER are unsupported. They are
            // rejected here so no provider, scanner, or executor path can accept them,
            // even if a target with an otherwise valid shape is supplied directly.
            CleanerCategory.EdgeCache => false,
            CleanerCategory.ChromeCache => false,
            CleanerCategory.FirefoxCache => false,
            CleanerCategory.UserWer => false,
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
        // V1 policy (F1-002): EdgeCache/ChromeCache/FirefoxCache/UserWer are unsupported.
        // Resolve() must not produce any target for them; IsAllowed() rejects them outright.
        return targets.AsReadOnly();
    }
    private static bool Simple(CleanerTarget target, string pattern, bool recurse, bool elevated) =>
        target.Pattern == pattern && target.Recurse == recurse && target.RequiresElevation == elevated;
    private static bool UnderOrEqual(string expected, string root, bool child)
    {
        if (string.Equals(CleanerSafety.Canonical(expected), CleanerSafety.Canonical(root), StringComparison.OrdinalIgnoreCase)) return true;
        return child && CleanerSafety.IsChild(expected, root);
    }
    private static bool IsProtected(string root) => new[] { Windows,
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        Path.Combine(Path.GetPathRoot(Windows)!, "System Volume Information"),
        Path.Combine(Path.GetPathRoot(Windows)!, "Boot"),
        Path.Combine(Path.GetPathRoot(Windows)!, "EFI") }
        .Any(protectedRoot => string.Equals(CleanerSafety.Canonical(protectedRoot), CleanerSafety.Canonical(root), StringComparison.OrdinalIgnoreCase) ||
            CleanerSafety.IsChild(protectedRoot, root));
}
