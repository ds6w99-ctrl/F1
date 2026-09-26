using SystemCore.Core.Uninstaller;

namespace SystemCore.Infrastructure.Uninstaller;

// No invented JSON/XML formats. These launchers have no verified local game
// metadata adapter yet. Registered applications remain available through Win32/MSI.
public abstract class UnverifiedGameLibraryProvider(IGameLibraryEnvironment environment, Source source) : IGameLibraryProvider
{
    public Task<GameLibraryResult> ReadAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var installed = environment.Locate(source).Installed;
        return Task.FromResult(new GameLibraryResult([], new(source, installed, 0, installed ? 1 : 0,
            installed ? "Unsupported/unverified metadata format" : "Not installed")));
    }
}
public sealed class EaGameLibraryProvider(IGameLibraryEnvironment environment) : UnverifiedGameLibraryProvider(environment, Source.EA);
public sealed class UbisoftGameLibraryProvider(IGameLibraryEnvironment environment) : UnverifiedGameLibraryProvider(environment, Source.Ubisoft);
public sealed class BattleNetGameLibraryProvider(IGameLibraryEnvironment environment) : UnverifiedGameLibraryProvider(environment, Source.BattleNet);

// PackageManager + AppListEntry are already the MSIX provider's authoritative
// inventory and authorization boundary. They do not prove that an app is a game.
// Do not relabel every Store app as Xbox, or read WindowsApps looking for configs.
public sealed class XboxGameLibraryProvider : IGameLibraryProvider
{
    public Task<GameLibraryResult> ReadAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return Task.FromResult(new GameLibraryResult([], new(Source.Xbox, false, 0, 1,
            "Game classification unverified; packages retained in MSIX inventory")));
    }
}
