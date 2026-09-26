using System.Text.Json;
using SystemCore.Core.Uninstaller;
using SystemCore.Infrastructure.Uninstaller;

internal static class GameDiscoveryTests
{
    private static string Steam(string id = "42", string name = "Synthetic لعبة 🎮", string directory = "Fixture", string state = "4") =>
        $"\"AppState\" {{ \"appid\" \"{id}\" \"name\" \"{name}\" \"installdir\" \"{directory}\" \"StateFlags\" \"{state}\" \"SizeOnDisk\" \"123456\" }}";
    private static string Epic(string path = @"D:\Epic\Fixture", bool incomplete = false) => JsonSerializer.Serialize(new
    {
        AppName = "fixture", CatalogItemId = "catalog42", CatalogNamespace = "namespace42", DisplayName = "Synthetic لعبة 🎮",
        InstallLocation = path, bIsIncompleteInstall = incomplete, InstallSize = 456789, AppVersionString = "1.2",
        LaunchExecutable = "bin/game.exe", MainGameAppName = "fixture"
    });

    public static async Task Run(Action<string, bool> check)
    {
        var environment = new FixtureEnvironment();
        var steam = new SteamGameLibraryProvider(environment);
        check("Steam launcher not installed", !(await steam.ReadAsync(default)).Status.LauncherDetected);
        environment.Configurations[Source.Steam] = new(true, @"C:\Steam");
        environment.Directories.Add(@"C:\Steam\steamapps");
        check("Steam installed without games does not guess", (await steam.ReadAsync(default)).Games.Count == 0);
        environment.AddSteam(@"C:\Steam", "42");
        var result = await steam.ReadAsync(default);
        check("Steam default library manifest discovered", result.Games.Count == 1);
        var game = result.Games.Single();
        check("Steam Unicode name retained", game.DisplayName == "Synthetic لعبة 🎮");
        check("Steam size comes only from manifest", game.EstimatedSize == 123456);
        check("Steam normalized identity and library", game.GameIdentity == "42" && game.LibraryPath == @"C:\Steam" && game.InstallState == "4");
        check("Managed games have no native command or uninstall permission", !game.CanUninstall && game.RegisteredUninstallCommand is null && game.ProductCode is null);
        check("Native presentation subset contract unchanged", !RemovableApplications.IsVisible(game));
        check("Read-only managed presentation is explicit", UnifiedLibraryPresentation.IsLauncherManaged(game) && UnifiedLibraryPresentation.IsVisible(game));
        check("Protected game never shown as managed", !UnifiedLibraryPresentation.IsVisible(game with { IsProtected = true }));
        check("Missing game identity cannot authorize managed presentation", !UnifiedLibraryPresentation.IsVisible(game with { GameIdentity = null }));

        environment.Metadata[@"C:\Steam\steamapps\libraryfolders.vdf"] = "\"libraryfolders\" { \"0\" { \"path\" \"C:\\\\Steam\" } \"1\" { \"path\" \"D:\\\\Library\" } }";
        environment.AddSteam(@"D:\Library", "43");
        result = await steam.ReadAsync(default);
        check("Steam multiple configured libraries", result.Games.Count == 2);
        check("Steam secondary library on D drive", result.Games.Any(g => g.InstallLocation == @"D:\Library\steamapps\common\Fixture"));
        check("No root-drive or exe search", environment.Searches.All(s => s.Pattern == "appmanifest_*.acf" && s.Directory.EndsWith("\\steamapps", StringComparison.Ordinal)));
        environment.Metadata[@"D:\Library\steamapps\appmanifest_44.acf"] = "broken";
        result = await steam.ReadAsync(default);
        check("Corrupt Steam manifest skipped with diagnostic", result.Games.Count == 2 && result.Status.UnsupportedOrUnverified == 1);
        check("Steam wrong manifest filename rejected", SteamGameLibraryProvider.ParseManifest(Steam(), @"C:\Steam", "appmanifest_43.acf") is null);
        check("Steam incomplete installation rejected", SteamGameLibraryProvider.ParseManifest(Steam(state: "2"), @"C:\Steam", "appmanifest_42.acf") is null);
        check("Steam path traversal rejected", SteamGameLibraryProvider.ParseManifest(Steam(directory: ".."), @"C:\Steam", "appmanifest_42.acf") is null);
        check("Steam duplicate identity fields rejected", SteamGameLibraryProvider.ParseManifest(Steam().Replace("\"appid\" \"42\"", "\"appid\" \"42\" \"appid\" \"43\""), @"C:\Steam", "appmanifest_42.acf") is null);
        check("Steam very large manifest rejected", SteamGameLibraryProvider.ParseManifest(new string(' ', 1024 * 1024 + 1), @"C:\Steam", "appmanifest_42.acf") is null);
        check("Steam missing reliable size stays unknown", SteamGameLibraryProvider.ParseManifest(Steam().Replace("123456", "invalid"), @"C:\Steam", "appmanifest_42.acf")?.EstimatedSize is null);
        check("No Steam protocol invented", game.RegisteredUninstallCommand is null && game.ManifestExecutable is null);

        var epic = new EpicGameLibraryProvider(environment);
        check("Epic launcher not installed", !(await epic.ReadAsync(default)).Status.LauncherDetected);
        environment.Configurations[Source.Epic] = new(true, ManifestDirectory: @"C:\EpicData\Manifests");
        environment.Directories.Add(@"C:\EpicData\Manifests");
        check("Epic missing manifest gives no guessed game", (await epic.ReadAsync(default)).Games.Count == 0);
        environment.Metadata[@"C:\EpicData\Manifests\fixture.item"] = Epic();
        check("Stale Epic manifest rejected", (await epic.ReadAsync(default)).Status.UnsupportedOrUnverified == 1);
        environment.Directories.Add(@"D:\Epic\Fixture");
        var epicResult = await epic.ReadAsync(default);
        check("Valid Epic manifest discovered", epicResult.Games.Count == 1);
        var epicGame = epicResult.Games.Single();
        check("Epic catalog identity version and size retained", epicGame.GameIdentity == "namespace42:catalog42:fixture" && epicGame.DisplayVersion == "1.2" && epicGame.EstimatedSize == 456789);
        check("Epic executable is icon metadata only", epicGame.ManifestExecutable == @"D:\Epic\Fixture\bin\game.exe" && epicGame.RegisteredUninstallCommand is null && !epicGame.CanUninstall);
        check("Epic incomplete install rejected", EpicGameLibraryProvider.ParseManifest(Epic(incomplete: true)) is null);
        check("Epic unsafe install location rejected", EpicGameLibraryProvider.ParseManifest(Epic(@"\\server\game")) is null);
        check("Game metadata never probes WindowsApps manually", EpicGameLibraryProvider.ParseManifest(Epic(@"C:\Program Files\WindowsApps\Fixture")) is null);
        check("Malformed Epic rejected", EpicGameLibraryProvider.ParseManifest("{broken}") is null);
        check("Epic huge manifest rejected", EpicGameLibraryProvider.ParseManifest(new string(' ', 1024 * 1024 + 1)) is null);
        check("Epic wrong type size stays unknown", EpicGameLibraryProvider.ParseManifest(Epic().Replace("456789", "\"456789\""))?.EstimatedSize is null);
        check("Epic duplicate key rejected", EpicGameLibraryProvider.ParseManifest(Epic().Replace("{", "{\"AppName\":\"other\",")) is null);

        var native = game with { Id = "native", ItemKind = ItemKind.Application, Source = Source.Win32, GameIdentity = null,
            RegistrySource = new(InventoryHive.CurrentUser, InventoryView.Registry64, "Steam App 42"), RegisteredUninstallCommand = "registered-native", CanUninstall = true };
        var merged = GameLibraryMerge.Merge(new([native], true, []), [new([game, game], new(Source.Steam, true, 2, 0, "Fixture"))]);
        check("Duplicate Steam game and native registration merged", merged.Applications.Count == 1 && merged.GameDuplicatesMerged == 2);
        check("Native identity and command preserved by merge", merged.Applications[0].Id == "native" && merged.Applications[0].RegisteredUninstallCommand == "registered-native");
        merged = GameLibraryMerge.Merge(new([native with { CanUninstall = false, InstallLocation = null }], true, []), [new([game], new(Source.Steam, true, 1, 0, "Fixture"))]);
        check("Native entry without location retains managed game presentation", UnifiedLibraryPresentation.IsLauncherManaged(merged.Applications.Single()) && merged.Applications.Single().InstallLocation is null);
        merged = GameLibraryMerge.Merge(new([native with { CanUninstall = false, IsProtected = true }], true, []), [new([game], new(Source.Steam, true, 1, 0, "Fixture"))]);
        check("Dedup cannot grant native removal rights", !merged.Applications.Single().CanUninstall && !UnifiedLibraryPresentation.IsVisible(merged.Applications.Single()));
        merged = GameLibraryMerge.Merge(new([native with { RegistrySource = null, InstallLocation = @"D:\Different" }], true, []), [new([game], new(Source.Steam, true, 1, 0, "Fixture"))]);
        check("Names alone never merge", merged.Applications.Count == 2);
        merged = GameLibraryMerge.Merge(new([], true, []), [new([game, game with { InstallLocation = @"D:\Conflicting" }], new(Source.Steam, true, 2, 0, "Fixture"))]);
        check("Conflicting duplicate identities quarantined", merged.Applications.Count == 0 && merged.GameDiscovery.Any(s => s.UnsupportedOrUnverified > 0));

        foreach (var source in new[] { Source.EA, Source.Ubisoft, Source.BattleNet })
        {
            IGameLibraryProvider provider = source switch { Source.EA => new EaGameLibraryProvider(environment), Source.Ubisoft => new UbisoftGameLibraryProvider(environment), _ => new BattleNetGameLibraryProvider(environment) };
            check(source + " absent launcher", !(await provider.ReadAsync(default)).Status.LauncherDetected);
            environment.Configurations[source] = new(true, @"D:\Launcher", @"D:\Launcher\Metadata");
            // A plausible synthetic manifest does not prove a real launcher format.
            environment.Metadata[@"D:\Launcher\Metadata\game.json"] = "{\"id\":\"42\",\"name\":\"Fixture\",\"installPath\":\"D:\\\\Game\"}";
            var unsupported = await provider.ReadAsync(default);
            check(source + " synthetic unknown format explicitly unsupported, not fabricated", unsupported.Games.Count == 0 && unsupported.Status.UnsupportedOrUnverified == 1);
        }
        var xbox = await new XboxGameLibraryProvider().ReadAsync(default);
        check("Xbox classification not guessed from package name", xbox.Games.Count == 0 && xbox.Status.UnsupportedOrUnverified == 1);
        var package = native with { ApplicationType = ApplicationType.Msix, Source = Source.Xbox, ItemKind = ItemKind.Game, PackageFullName = "Fixture_1.0.0.0_x64__fixture" };
        check("Genuine removable Xbox package uses native gate", RemovableApplications.IsVisible(package));
        check("Xbox framework and protected packages hidden", !UnifiedLibraryPresentation.IsVisible(package with { IsFramework = true }) && !UnifiedLibraryPresentation.IsVisible(package with { IsProtected = true }));
        check("No guessed launcher action", !new GameLauncher(environment).CanOpen(Source.Steam));
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        try { await steam.ReadAsync(cancelled.Token); check("Game discovery cancellation", false); }
        catch (OperationCanceledException) { check("Game discovery cancellation", true); }
        using var executor = new UninstallExecutor(new FixedInventory(new([game], true, [])), new NeverRunner(), new UninstallCompletionDetector(new FixedInventory(new([game], true, []))));
        var session = await executor.RefreshAsync(default);
        check("Managed game cannot reach destructive native runner", (await executor.UninstallAsync(new(session.Id, game.Id), null, default)).State == UninstallState.Failed);
        var evidence = new ApplicationRemovalEvidence();
        var oldNative = native with { DisplayName = "F1-Game-Evidence-" + Guid.NewGuid().ToString("N"), InstallLocation = null, Publisher = null };
        await evidence.CaptureAsync(oldNative, new([oldNative], true, []), default);
        await evidence.CaptureAsync(game with { Id = oldNative.Id }, new([game], true, []), default);
        check("Game classification clears any earlier native leftover evidence", evidence.ConfirmRemoved(oldNative.Id) is null);
        check("Game disappearance without launcher verification is unknown", await new UninstallCompletionDetector(new FixedInventory(new([], true, []))).VerifyAsync(game, default) == UninstallState.Unknown);
        var verifiedEmpty = new InventoryResult([], true, []) { GameDiscovery = [new(Source.Steam, true, 0, 0, "Verified metadata")] };
        check("Game completion needs verified native and manifest absence", await new UninstallCompletionDetector(new FixedInventory(verifiedEmpty)).VerifyAsync(game, default) == UninstallState.Removed);
        check("Stale game manifest prevents completion", await new UninstallCompletionDetector(new FixedInventory(new([game], true, []))).VerifyAsync(game, default) == UninstallState.StillInstalled);
    }

    private sealed class FixedInventory(InventoryResult result) : IInstalledApplicationInventory
    { public Task<InventoryResult> ReadAsync(CancellationToken token) => Task.FromResult(result); }
    private sealed class NeverRunner : INativeUninstallRunner
    { public Task<int?> RunAsync(InstalledApplication application, CancellationToken token) => throw new Exception("Managed game must never launch uninstall"); }
    private sealed class FixtureEnvironment : IGameLibraryEnvironment
    {
        public Dictionary<Source, GameLauncherConfiguration> Configurations { get; } = [];
        public Dictionary<string, string> Metadata { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> Directories { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<(string Directory, string Pattern)> Searches { get; } = [];
        public GameLauncherConfiguration Locate(Source source) => Configurations.GetValueOrDefault(source) ?? new(false);
        public string? ReadMetadata(string path) => Metadata.GetValueOrDefault(path);
        public bool DirectoryExists(string path) => Directories.Contains(path);
        public IEnumerable<string> ManifestFiles(string directory, string pattern)
        {
            Searches.Add((directory, pattern));
            return Metadata.Keys.Where(p => Path.GetDirectoryName(p) == directory && System.IO.Enumeration.FileSystemName.MatchesSimpleExpression(pattern, Path.GetFileName(p))).ToArray();
        }
        public void AddSteam(string library, string id)
        {
            Directories.Add(library + @"\steamapps");
            Directories.Add(library + @"\steamapps\common\Fixture");
            Metadata[library + @"\steamapps\appmanifest_" + id + ".acf"] = Steam(id);
        }
    }
}
