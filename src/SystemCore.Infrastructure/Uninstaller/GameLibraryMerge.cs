using SystemCore.Core.Uninstaller;

namespace SystemCore.Infrastructure.Uninstaller;

public static class GameLibraryMerge
{
    public static InventoryResult Merge(InventoryResult native, IReadOnlyList<GameLibraryResult> discoveries)
    {
        var rows = native.Applications.ToList();
        var merged = 0;
        var statuses = discoveries.Select(d => d.Status).ToList();
        foreach (var group in discoveries.SelectMany(d => d.Games).GroupBy(g => (g.Source, g.GameIdentity)))
        {
            var games = group.ToArray();
            var game = games[0];
            if (game.GameIdentity is null || !UnifiedLibraryPresentation.IsLauncherManaged(game)) continue;
            if (games.Any(g => !SamePath(game.InstallLocation, g.InstallLocation)))
            {
                statuses.Add(new(game.Source, true, 0, games.Length, "Conflicting game identities"));
                continue;
            }
            merged += games.Length - 1;
            var peers = rows.Where(a => NativeLink(a, game)).ToArray();
            if (peers.Length == 1)
            {
                var peer = peers[0];
                // Only presentation metadata is enriched. Native identity, permissions,
                // restrictions, uninstall command and ownership paths remain untouched.
                rows[rows.IndexOf(peer)] = peer with
                {
                    ItemKind = ItemKind.Game, Source = game.Source, GameIdentity = game.GameIdentity,
                    GameInstallLocation = game.GameInstallLocation,
                    LibraryPath = game.LibraryPath, InstallState = game.InstallState,
                    EstimatedSize = game.EstimatedSize ?? peer.EstimatedSize,
                    ManifestExecutable = game.ManifestExecutable, LauncherIcon = game.LauncherIcon
                };
                merged++;
            }
            else rows.Add(game); // Ambiguous native identities are never folded by name.
        }
        return native with
        {
            Applications = rows.OrderBy(a => a.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToArray(),
            GameDiscovery = statuses, GameDuplicatesMerged = merged
        };
    }
    private static bool NativeLink(InstalledApplication app, InstalledApplication game)
    {
        if (app.ItemKind == ItemKind.Game) return app.Source == game.Source && app.GameIdentity == game.GameIdentity;
        if (game.Source == Source.Steam && app.RegistrySource?.KeyName == "Steam App " + game.GameIdentity) return true;
        return SamePath(app.InstallLocation, game.InstallLocation);
    }
    private static bool SamePath(string? left, string? right) => GameMetadata.LocalPath(left) is { } a && a.Length > 3
        && GameMetadata.LocalPath(right) is { } b && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
