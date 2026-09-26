using SystemCore.Core.Cleaner;

namespace SystemCore.Infrastructure.Cleaner;

public sealed class WindowsCleanerTargets : ICleanerTargetProvider
{
    internal static bool IsAllowed(CleanerTarget target)
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var expected = target.Category switch
        {
            CleanerCategory.UserTemp => Path.Combine(local, "Temp"),
            CleanerCategory.WindowsTemp => Path.Combine(windows, "Temp"),
            CleanerCategory.ShaderCache => Path.Combine(local, "D3DSCache"),
            CleanerCategory.CrashDumps => Path.Combine(local, "CrashDumps"),
            CleanerCategory.Thumbnails => Path.Combine(local, "Microsoft", "Windows", "Explorer"),
            _ => null
        };
        if (expected is null || target.CanDeleteDirectories || target.MinimumAge < TimeSpan.FromHours(24)) return false;
        if (target.Category != CleanerCategory.WindowsTemp)
        {
            foreach (var protectedRoot in new[] { windows,
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Path.Combine(Path.GetPathRoot(windows)!, "System Volume Information"),
                Path.Combine(Path.GetPathRoot(windows)!, "Boot"),
                Path.Combine(Path.GetPathRoot(windows)!, "EFI") })
            {
                if (string.Equals(CleanerSafety.Canonical(protectedRoot), CleanerSafety.Canonical(target.Root), StringComparison.OrdinalIgnoreCase) ||
                    CleanerSafety.IsChild(protectedRoot, target.Root)) return false;
            }
        }
        var same = string.Equals(CleanerSafety.Canonical(expected), CleanerSafety.Canonical(target.Root), StringComparison.OrdinalIgnoreCase);
        // A narrower UserTemp root is useful for isolated tests and cannot expand the allowlist.
        if (!same && !(target.Category == CleanerCategory.UserTemp && CleanerSafety.IsChild(expected, target.Root))) return false;
        return target.Category switch
        {
            CleanerCategory.CrashDumps => target.Pattern == "*.dmp" && !target.Recurse,
            CleanerCategory.Thumbnails => target.Pattern == "thumbcache_*.db" && !target.Recurse,
            _ => target.Pattern == "*"
        };
    }
    public IReadOnlyList<CleanerTarget> Resolve()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var targets = new List<CleanerTarget>();
        void Add(CleanerCategory category, string root, string pattern, bool recurse, bool selected, bool review = false, bool elevated = false)
        {
            if (CleanerSafety.Canonical(root) is not null)
                targets.Add(new(category, root, pattern, recurse, TimeSpan.FromHours(24), selected, elevated, review));
        }
        // Environment TEMP can be redirected to arbitrary personal/protected data. Only accept
        // the exact conventional known-folder Temp root, never blindly trust the environment.
        var temp = Path.Combine(local, "Temp");
        if (string.Equals(CleanerSafety.Canonical(Path.GetTempPath()), CleanerSafety.Canonical(temp), StringComparison.OrdinalIgnoreCase))
            Add(CleanerCategory.UserTemp, temp, "*", true, true);
        Add(CleanerCategory.WindowsTemp, Path.Combine(windows, "Temp"), "*", true, false, elevated: true);
        Add(CleanerCategory.ShaderCache, Path.Combine(local, "D3DSCache"), "*", true, true);
        Add(CleanerCategory.CrashDumps, Path.Combine(local, "CrashDumps"), "*.dmp", false, false, review: true);
        Add(CleanerCategory.Thumbnails, Path.Combine(local, "Microsoft", "Windows", "Explorer"), "thumbcache_*.db", false, false, review: true);
        // Browser, WER and Recycle Bin are explicitly unavailable until their distinct layout/
        // Shell contracts are validated. Do not broaden filesystem patterns to approximate them.
        return targets.AsReadOnly();
    }
}
