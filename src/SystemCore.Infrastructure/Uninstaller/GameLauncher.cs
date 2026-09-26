using System.Diagnostics;
using SystemCore.Core.Uninstaller;

namespace SystemCore.Infrastructure.Uninstaller;

// Opens only the freshly resolved, registered launcher executable with NO arguments.
// No URI construction, command forwarding or game executable launch is supported.
public sealed class GameLauncher(IGameLibraryEnvironment environment) : IGameLauncher
{
    private string? Executable(Source source)
    {
        if (source is not (Source.Steam or Source.Epic or Source.EA or Source.Ubisoft or Source.BattleNet)) return null;
        var configuration = environment.Locate(source);
        return configuration.Installed && GameMetadata.LocalPath(configuration.Executable) is { } path && UninstallCommandParser.IsSafeExecutable(path) ? path : null;
    }
    public bool CanOpen(Source source) => Executable(source) is not null;
    public Task<bool> OpenAsync(Source source, CancellationToken token) => Task.Run(() =>
    {
        token.ThrowIfCancellationRequested();
        try
        {
            if (Executable(source) is not { } executable) return false;
            using var ancestors = Cleaner.PinnedCleanerPath.Open(Path.GetDirectoryName(executable)!, true);
            using var image = new FileStream(executable, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (!UninstallCommandParser.IsSafeExecutable(executable)) return false;
            token.ThrowIfCancellationRequested();
            using var process = Process.Start(new ProcessStartInfo(executable) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(executable)! });
            return process is not null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception) { return false; }
    }, token);
}
