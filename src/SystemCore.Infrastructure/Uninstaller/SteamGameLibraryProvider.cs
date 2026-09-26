using System.Globalization;
using SystemCore.Core.Uninstaller;

namespace SystemCore.Infrastructure.Uninstaller;

public sealed class SteamGameLibraryProvider(IGameLibraryEnvironment environment) : IGameLibraryProvider
{
    public Task<GameLibraryResult> ReadAsync(CancellationToken token) => Task.Run(() =>
    {
        var config = environment.Locate(Source.Steam);
        var games = new List<InstalledApplication>();
        var rejected = 0;
        if (!config.Installed || GameMetadata.LocalPath(config.Root) is not { } root)
            return new GameLibraryResult([], new(Source.Steam, false, 0, 0, "Not installed"));
        var libraries = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { root };
        try
        {
            var text = environment.ReadMetadata(Path.Combine(root, "steamapps", "libraryfolders.vdf"));
            if (text is not null)
            {
                var folders = ValveMetadata.Parse(text)?.Child("libraryfolders");
                if (folders is null) rejected++;
                else foreach (var pair in folders.Values)
                {
                    if (!uint.TryParse(pair.Key, out _)) continue;
                    var path = pair.Value is ValveMetadata.Node node ? node.Text("path") : pair.Value as string;
                    if (GameMetadata.LocalPath(path) is { } library && libraries.Count < 64) libraries.Add(library);
                    else rejected++;
                }
            }
            var visited = 0;
            foreach (var library in libraries)
            {
                token.ThrowIfCancellationRequested();
                var directory = Path.Combine(library, "steamapps");
                if (!environment.DirectoryExists(directory)) { rejected++; continue; }
                foreach (var file in environment.ManifestFiles(directory, "appmanifest_*.acf"))
                {
                    token.ThrowIfCancellationRequested();
                    if (++visited > 4096) { rejected++; break; }
                    try
                    {
                        var manifest = environment.ReadMetadata(file);
                        var app = manifest is null ? null : ParseManifest(manifest, library, Path.GetFileName(file));
                        if (app is null || !environment.DirectoryExists(app.InstallLocation!)) { rejected++; continue; }
                        games.Add(app with { LauncherIcon = config.Executable });
                    }
                    catch (Exception e) when (MetadataFailure(e)) { rejected++; }
                }
                if (visited > 4096) break;
            }
        }
        catch (Exception e) when (MetadataFailure(e)) { rejected++; }
        return new GameLibraryResult(games, new(Source.Steam, true, games.Count, rejected, rejected == 0 ? "Verified metadata" : "Partial metadata"));
    }, token);

    internal static bool MetadataFailure(Exception e) => e is IOException or UnauthorizedAccessException or ArgumentException or System.Text.DecoderFallbackException or System.ComponentModel.Win32Exception;
    public static InstalledApplication? ParseManifest(string text, string library, string fileName)
    {
        var state = ValveMetadata.Parse(text)?.Child("AppState");
        var id = state?.Text("appid");
        var name = state?.Text("name");
        var directory = state?.Text("installdir");
        if (!uint.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var appId) || appId == 0
            || fileName != $"appmanifest_{appId}.acf" || !GameMetadata.Name(name) || string.IsNullOrWhiteSpace(directory)
            || directory.IndexOfAny(['\\', '/', ':', '"', '*', '?', '<', '>', '|']) >= 0
            || GameMetadata.LocalPath(library) is null
            || !uint.TryParse(state!.Text("StateFlags"), out var flags) || flags != 4) return null;
        var path = GameMetadata.LocalPath(Path.Combine(library, "steamapps", "common", directory));
        if (path is null) return null;
        long? size = long.TryParse(state.Text("SizeOnDisk"), NumberStyles.None, CultureInfo.InvariantCulture, out var bytes) && bytes >= 0 ? bytes : null;
        return GameMetadata.Game(Source.Steam, appId.ToString(CultureInfo.InvariantCulture), name!, path, size)
            with { LibraryPath = library, InstallState = flags.ToString(CultureInfo.InvariantCulture) };
    }
}

// Small bounded KeyValues parser. Duplicate keys, truncated objects, excessive nesting
// and unsupported escape sequences fail closed instead of guessing partial identities.
internal static class ValveMetadata
{
    internal sealed class Node
    {
        public Dictionary<string, object> Values { get; } = new(StringComparer.OrdinalIgnoreCase);
        public string? Text(string key) => Values.GetValueOrDefault(key) as string;
        public Node? Child(string key) => Values.GetValueOrDefault(key) as Node;
    }
    public static Node? Parse(string text)
    {
        if (text.Length > WindowsGameLibraryEnvironment.MaximumManifestBytes) return null;
        var offset = 0; var count = 0;
        string? Next()
        {
            while (offset < text.Length)
            {
                if (char.IsWhiteSpace(text[offset])) { offset++; continue; }
                if (text[offset] == '/' && offset + 1 < text.Length && text[offset + 1] == '/')
                { while (offset < text.Length && text[offset] != '\n') offset++; continue; }
                break;
            }
            if (offset == text.Length) return null;
            if (++count > 65536) throw new FormatException();
            var c = text[offset++];
            if (c is '{' or '}') return c.ToString();
            if (c != '"') throw new FormatException();
            var value = new System.Text.StringBuilder();
            while (offset < text.Length)
            {
                c = text[offset++];
                if (c == '"') return value.ToString();
                if (c == '\\')
                {
                    if (offset == text.Length) throw new FormatException();
                    c = text[offset++];
                    if (c is not ('\\' or '"')) throw new FormatException();
                }
                if (char.IsControl(c) || value.Length >= 4096) throw new FormatException();
                value.Append(c);
            }
            throw new FormatException();
        }
        Node Read(int depth)
        {
            if (depth > 16) throw new FormatException();
            var node = new Node();
            while (Next() is { } key)
            {
                if (key == "}") { if (depth == 0) throw new FormatException(); return node; }
                if (key == "{") throw new FormatException();
                var value = Next() ?? throw new FormatException();
                if (value == "}" || !node.Values.TryAdd(key, value == "{" ? Read(depth + 1) : value)) throw new FormatException();
            }
            if (depth != 0) throw new FormatException();
            return node;
        }
        try { return Read(0); }
        catch (FormatException) { return null; }
    }
}
