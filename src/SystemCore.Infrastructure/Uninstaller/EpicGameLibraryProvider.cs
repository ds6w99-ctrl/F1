using System.Text.Json;
using SystemCore.Core.Uninstaller;

namespace SystemCore.Infrastructure.Uninstaller;

public sealed class EpicGameLibraryProvider(IGameLibraryEnvironment environment) : IGameLibraryProvider
{
    public Task<GameLibraryResult> ReadAsync(CancellationToken token) => Task.Run(() =>
    {
        var config = environment.Locate(Source.Epic);
        if (!config.Installed) return new GameLibraryResult([], new(Source.Epic, false, 0, 0, "Not installed"));
        var games = new List<InstalledApplication>(); var rejected = 0; var visited = 0;
        try
        {
            if (config.ManifestDirectory is null || !environment.DirectoryExists(config.ManifestDirectory)) rejected++;
            else foreach (var file in environment.ManifestFiles(config.ManifestDirectory, "*.item"))
            {
                token.ThrowIfCancellationRequested();
                if (++visited > 4096) { rejected++; break; }
                try
                {
                    var text = environment.ReadMetadata(file);
                    var game = text is null ? null : ParseManifest(text);
                    if (game is null || !environment.DirectoryExists(game.InstallLocation!)) { rejected++; continue; }
                    games.Add(game with { LauncherIcon = config.Executable });
                }
                catch (Exception e) when (SteamGameLibraryProvider.MetadataFailure(e)) { rejected++; }
            }
        }
        catch (Exception e) when (SteamGameLibraryProvider.MetadataFailure(e)) { rejected++; }
        return new GameLibraryResult(games, new(Source.Epic, true, games.Count, rejected, rejected == 0 ? "Verified metadata" : "Partial metadata"));
    }, token);

    public static InstalledApplication? ParseManifest(string text)
    {
        if (text.Length > WindowsGameLibraryEnvironment.MaximumManifestBytes) return null;
        try
        {
            using var document = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 16 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Select(p => p.Name).Distinct(StringComparer.Ordinal).Count() != root.EnumerateObject().Count()) return null;
            string? Get(string key) => root.TryGetProperty(key, out var field) && field.ValueKind == JsonValueKind.String ? field.GetString() : null;
            var id = Get("AppName"); var name = Get("DisplayName");
            var catalog = Get("CatalogItemId"); var space = Get("CatalogNamespace");
            var path = GameMetadata.LocalPath(Get("InstallLocation"));
            if (!GameMetadata.Identity(id) || !GameMetadata.Identity(catalog) || !GameMetadata.Identity(space) || !GameMetadata.Name(name) || path is null
                || !root.TryGetProperty("bIsIncompleteInstall", out var incomplete) || incomplete.ValueKind != JsonValueKind.False) return null;
            // DLC metadata does not establish a standalone installed game.
            if (Get("MainGameAppName") is { Length: > 0 } main && !string.Equals(main, id, StringComparison.Ordinal)) return null;
            long? size = root.TryGetProperty("InstallSize", out var amount) && amount.ValueKind == JsonValueKind.Number && amount.TryGetInt64(out var bytes) && bytes >= 0 ? bytes : null;
            var executable = Get("LaunchExecutable");
            var exePath = executable is not null && !Path.IsPathRooted(executable) ? GameMetadata.LocalPath(Path.Combine(path, executable.Replace('/', '\\'))) : null;
            var app = GameMetadata.Game(Source.Epic, $"{space}:{catalog}:{id}", name!, path, size, Get("AppVersionString"));
            return app with { ManifestExecutable = exePath, InstallState = "Installed" };
        }
        catch (JsonException) { return null; }
        catch (ArgumentException) { return null; }
    }
}
