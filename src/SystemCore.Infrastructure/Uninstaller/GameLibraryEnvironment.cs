using Microsoft.Win32;
using SystemCore.Core.Uninstaller;

namespace SystemCore.Infrastructure.Uninstaller;

public sealed record GameLauncherConfiguration(bool Installed, string? Root = null, string? ManifestDirectory = null, string? Executable = null);

// Only known metadata locations are exposed. No recursive or drive enumeration API.
public interface IGameLibraryEnvironment
{
    GameLauncherConfiguration Locate(Source source);
    string? ReadMetadata(string path);
    IEnumerable<string> ManifestFiles(string directory, string pattern);
    bool DirectoryExists(string path);
}

public sealed class WindowsGameLibraryEnvironment : IGameLibraryEnvironment
{
    public const int MaximumManifestBytes = 1024 * 1024;
    public GameLauncherConfiguration Locate(Source source)
    {
        if (source == Source.Steam)
        {
            var root = Value(RegistryHive.CurrentUser, @"Software\Valve\Steam", "SteamPath")
                ?? Value(RegistryHive.LocalMachine, @"Software\Valve\Steam", "InstallPath");
            root = GameMetadata.LocalPath(root?.Replace('/', '\\'));
            var executable = GameMetadata.LocalPath(Value(RegistryHive.CurrentUser, @"Software\Valve\Steam", "SteamExe")?.Replace('/', '\\'));
            return new(root is not null && DirectoryExists(root), root, Executable: executable);
        }
        var image = source switch
        {
            Source.Epic => "EpicGamesLauncher.exe", Source.EA => "EADesktop.exe",
            Source.Ubisoft => "UbisoftConnect.exe", Source.BattleNet => "Battle.net.exe", _ => null
        };
        var exe = image is null ? null : Value(RegistryHive.LocalMachine, @"Software\Microsoft\Windows\CurrentVersion\App Paths\" + image, "")
            ?? Value(RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\App Paths\" + image, "");
        exe = GameMetadata.LocalPath(exe);
        var rootPath = source switch
        {
            Source.Epic => Value(RegistryHive.LocalMachine, @"Software\Epic Games\EpicGamesLauncher", "AppDataPath"),
            Source.Ubisoft => Value(RegistryHive.LocalMachine, @"Software\Ubisoft\Launcher", "InstallDir"),
            Source.EA => Value(RegistryHive.LocalMachine, @"Software\Electronic Arts\EA Desktop", "InstallLocation"),
            Source.BattleNet => Value(RegistryHive.LocalMachine, @"Software\Blizzard Entertainment\Battle.net", "InstallPath"),
            _ => null
        };
        if (source == Source.Epic)
        {
            // Epic's known ProgramData metadata root; not a search for game directories.
            var manifests = Path.Combine(rootPath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Epic", "EpicGamesLauncher", "Data"), "Manifests");
            return new(DirectoryExists(manifests) || ApplicationIconReader.Parse(exe) is not null, rootPath, manifests, exe);
        }
        return new((rootPath is not null && DirectoryExists(rootPath)) || ApplicationIconReader.Parse(exe) is not null, rootPath, Executable: exe);
    }

    private static string? Value(RegistryHive hive, string key, string name)
    {
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(hive, view);
                using var entry = root.OpenSubKey(key);
                if (entry?.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) is string text && text.Length <= 1024) return text;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
        }
        return null;
    }

    public bool DirectoryExists(string path) => SafeAncestry(path) && Directory.Exists(path);
    public IEnumerable<string> ManifestFiles(string directory, string pattern)
    {
        if (!DirectoryExists(directory)) return [];
        // Provider enforces a total manifest budget as well.
        return Directory.EnumerateFiles(directory, pattern, SearchOption.TopDirectoryOnly).Take(4097);
    }
    public string? ReadMetadata(string path)
    {
        if (!SafeAncestry(path) || !File.Exists(path)) return null;
        using var lease = Cleaner.PinnedCleanerPath.Open(path, false, shareRead: true);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > MaximumManifestBytes) return null;
        using var reader = new StreamReader(stream, new System.Text.UTF8Encoding(false, true), true);
        return reader.ReadToEnd();
    }
    private static bool SafeAncestry(string path)
    {
        if (GameMetadata.LocalPath(path) is null) return false;
        try
        {
            if (new DriveInfo(path[..3]).DriveType != DriveType.Fixed) return false;
            for (var node = path; node is not null; node = Path.GetDirectoryName(node))
                if ((File.GetAttributes(node) & FileAttributes.ReparsePoint) != 0) return false;
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { return false; }
    }
}

internal static class GameMetadata
{
    public static string? LocalPath(string? value)
    {
        if (value is null || value.Length is < 4 or > 240 || !char.IsAsciiLetter(value[0]) || value[1] != ':' || value[2] != '\\'
            || value[3..].Any(c => char.IsControl(c) || c is ':' or '"' or '/' or '*' or '?' or '<' or '>' or '|')
            || value.Split('\\').Any(p => p is "." or ".." || p.Equals("WindowsApps", StringComparison.OrdinalIgnoreCase) || p.EndsWith(' ') || p.EndsWith('.'))) return null;
        return value.TrimEnd('\\');
    }
    public static bool Identity(string? value) => value is { Length: > 0 and <= 160 } && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.');
    public static InstalledApplication Game(Source source, string id, string name, string path, long? size, string? version = null) =>
        new($"game:{source}:{id}", name, null, version, null, path, size, null, ApplicationType.Win32,
            null, false, null, null, null, null, null, false, false, false, false, "Launcher managed")
        { ItemKind = ItemKind.Game, Source = source, GameIdentity = id, GameInstallLocation = path };
    public static bool Name(string? name) => name is { Length: > 0 and <= 512 } && !string.IsNullOrWhiteSpace(name) && !name.Any(char.IsControl);
}
