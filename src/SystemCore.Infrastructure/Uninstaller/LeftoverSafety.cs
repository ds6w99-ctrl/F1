using SystemCore.Core.Cleaner;
using SystemCore.Core.Uninstaller;

namespace SystemCore.Infrastructure.Uninstaller;

public static class LeftoverSafety
{
    private static readonly string[] SharedNames = ["Microsoft", "Adobe", "Google", "NVIDIA Corporation", "Steam", "Epic Games", "Epic", "EA", "Ubisoft", "Battle.net", "Common Files", "Packages", "Temp", "Cache", "Apps", "Programs", "Windows", "Users", "Documents", "Desktop", "Downloads"];
    private static readonly string[] SharedGameNames = ["steam", "steamlibrary", "steamapps", "epic", "epicgames", "epicgameslauncher", "ea", "eagames", "eadesktop", "ubisoft", "ubisoftgamelauncher", "battlenet", "launcher", "launchers", "launcherroot",
            "shadercache", "shadercaches", "sharedshadercache", "dxcache", "glcache", "d3dscache", "anticheat", "easyanticheat", "battleye", "sharedanticheat",
            "redistributable", "redistributables", "sharedredistributable", "sharedredistributables", "commonredist", "redist", "mods", "sharedmods", "sharedmodsdirectory", "workshop", "sharedworkshop", "workshopcontent", "sharedworkshopcontent"];
    private static readonly string[] PersonalMarkers = ["save", "screenshot", "mod", "profile", "config", "setting", "userdata", "usergenerated", "usercontent", "ugc", "custom", "document", "project", "preset"];
    private static readonly string[] PersonalExtensions = [".sav", ".save", ".ini", ".cfg", ".conf", ".config", ".json", ".xml", ".yaml", ".yml", ".db", ".sqlite", ".png", ".jpg", ".jpeg", ".webp", ".bmp", ".tiff", ".mp4"];
    private static readonly string[] ApplicationBinaryExtensions = [".exe", ".dll", ".mui", ".pdb", ".cat"];
    private static readonly string[] RegistryIdentityValues = ["Publisher", "DisplayName", "DisplayVersion", "InstallLocation", "DataPath", "ProductCode", "PackageFamilyName", "VersionMajor", "VersionMinor"];
    private static string Compact(string value) => new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
    public static bool SharedGamePath(string path) => path.Split(['\\', '/']).Any(part => SharedGameNames.Contains(Compact(part), StringComparer.Ordinal));
    // Ownership alone cannot establish whether user-created data is disposable.
    // A personal directory taints every descendant, including opaque binary saves.
    public static bool MayContainPersonalData(string path) => path.Split(['\\', '/']).Select(Compact)
        .Any(part => PersonalMarkers.Any(marker => part.Contains(marker, StringComparison.Ordinal)))
        || PersonalExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
    public static bool HasApplicationBinaryExtension(string path) => ApplicationBinaryExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
    public static bool IsRegistryIdentityValue(string name) => RegistryIdentityValues.Contains(name, StringComparer.OrdinalIgnoreCase);
    public static bool SharedComponent(InstalledApplication application) => application.IsProtected || application.IsFramework || application.IsSystemComponent
        || SharedNames.Contains(application.DisplayName, StringComparer.OrdinalIgnoreCase)
        || new[] { "runtime", "redistributable", "framework", "driver", "anti-cheat", "anticheat", "codec", "sdk", "licensing", "update service" }.Any(word => application.DisplayName.Contains(word, StringComparison.OrdinalIgnoreCase));
    public static bool CanOwnDirectory(InstalledApplication application, string path, IEnumerable<InstalledApplication> inventory)
    {
        var canonical = CleanerSafety.Canonical(path);
        if (canonical is null || canonical.Length <= 3 || SharedComponent(application)) return false;
        if (application.ItemKind == ItemKind.Game && SharedGamePath(canonical)) return false;
        var roots = new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.LocalApplicationData,
            Environment.SpecialFolder.ApplicationData, Environment.SpecialFolder.CommonApplicationData };
        if (!roots.Select(Environment.GetFolderPath).Where(r => r.Length > 0).Any(r => CleanerSafety.IsChild(r, canonical))) return false;
        var leaf = Path.GetFileName(canonical);
        if (SharedNames.Contains(leaf, StringComparer.OrdinalIgnoreCase) || string.Equals(leaf, application.Publisher, StringComparison.OrdinalIgnoreCase)) return false;
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var protectedTrees = new[] { Path.Combine(local, "Packages"), Path.Combine(local, "Microsoft", "Windows"),
            Path.Combine(local, "Temp"), Path.Combine(local, "SystemCore"), Path.Combine(roaming, "Microsoft", "Windows"),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFilesX86),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsApps"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Package Cache"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Microsoft", "Windows") };
        if (protectedTrees.Where(p => !string.IsNullOrWhiteSpace(p)).Any(p => Conflicts(p, canonical))) return false;
        // Protect the operating-system subtree even if a hostile registration claims it.
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (string.Equals(canonical, windows, StringComparison.OrdinalIgnoreCase) || CleanerSafety.IsChild(windows, canonical)) return false;
        return !inventory.Where(a => a.Id != application.Id).Any(a => a.InstallLocation is { } other && Conflicts(canonical, other));
    }
    public static bool Conflicts(string first, string second) => string.Equals(CleanerSafety.Canonical(first), CleanerSafety.Canonical(second), StringComparison.OrdinalIgnoreCase)
        || CleanerSafety.IsChild(first, second) || CleanerSafety.IsChild(second, first);
    public static bool SafeRegistryName(string value) => value.Length is > 0 and <= 100 && value != "." && value != ".."
        && value.IndexOfAny(['\\', '/', ':', '\0']) < 0 && !value.Any(char.IsControl)
        && !new[] { "Classes", "CLSID", "Interface", "TypeLib", "AppID", "Services", "Drivers", "Policies", "Installer", "Microsoft", "SharedDLLs", "KnownDLLs", "SystemCore", "Component Based Servicing", "Shell" }.Contains(value, StringComparer.OrdinalIgnoreCase);
}
